using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.SerializationLogic;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 只带 TypeTree、没有 C# 类接管的资源（典型是 <c>globalgamemanagers</c> 里的
/// PlayerSettings，ClassID 129）在读取时会因为类型树与二进制不匹配而整体失败。
///
/// <para>上游对它们的处理是"整条资源作废"：<c>GameAssetFactory.TryReadNormalObject</c>
/// 捕获异常后返回一个 <see cref="UnreadableObject"/>，其中所有字段都丢失。可这类资源的
/// 类型树往往是旧版本残留（PlayerSettings 的平台列表在不同版本里长度不同），
/// 真正能读的字段远比不能读的多。</para>
///
/// <para>这里改成"尽量读"：第一次仍走原方法，原方法抛异常时改成逐字段读，
/// 某个字段越界就跳过它继续读下一个，其余字段仍然有值。</para>
///
/// <para>目标 <c>ReadRelease</c>/<c>ReadEditor</c> 声明在 <see cref="TypeTreeObject"/>
/// 的私有嵌套类里，注解无法命名，由
/// <see cref="PatchRegistrar.RegisterTypeTreeReaders"/> 按反射签名注册。</para>
/// </summary>
internal static class TypeTreeReadPatch
{
    /// <summary>容错重读自身也会经过同一个 Prefix，用这个标记防重入。</summary>
    [ThreadStatic]
    private static bool t_lenient;

    private static int s_attempts;
    private static int s_recovered;
    private static int s_failed;

    private static readonly HashSet<string> LoggedFailures = new();
    private static readonly object LoggedFailuresLock = new();

    private static readonly Dictionary<Type, FieldInfo?> ResolverFields = new();

    /// <summary>容错读取的累计统计。</summary>
    public static (int Attempts, int Recovered, int Failed) LenientStats =>
        (Volatile.Read(ref s_attempts), Volatile.Read(ref s_recovered), Volatile.Read(ref s_failed));

    public static void ReportLenientStats()
    {
        (int attempts, int recovered, int failed) = LenientStats;
        if (attempts == 0)
        {
            return;
        }
        Console.WriteLine($"[TypeTreeRead] lenient retry: attempts={attempts} recovered={recovered} failed={failed}");
    }

    public static bool LenientReadRelease(TypeTreeObject __instance, ref EndianSpanReader reader)
        => TryLenient(__instance, ref reader, editor: false);

    public static bool LenientReadEditor(TypeTreeObject __instance, ref EndianSpanReader reader)
        => TryLenient(__instance, ref reader, editor: true);

    /// <summary>
    /// Prefix: 先让原方法跑一次；它抛异常时才从头逐字段读。
    ///
    /// <para>返回 <c>false</c> 表示原方法没成功但我们已经处理了（跳过原方法）；
    /// 返回 <c>true</c> 表示继续走原方法——正常情况，以及容错也没读出东西时。
    /// 修不好就该让它照常失败，而不是假装成功。</para>
    /// </summary>
    private static bool TryLenient(TypeTreeObject instance, ref EndianSpanReader reader, bool editor)
    {
        if (t_lenient)
        {
            // 容错重读自己：直接跳过原方法。
            return false;
        }

        int start = reader.Position;
        TransferInstructionFlags flags = instance.Collection.Flags;
        ITypeResolver? resolver = FindResolver(instance);
        SerializableStructure fields = editor ? instance.EditorFields : instance.ReleaseFields;

        try
        {
            fields.Read(ref reader, instance.Collection.Version, flags, resolver!);
            return true;
        }
        catch (Exception original)
        {
            Interlocked.Increment(ref s_attempts);
            t_lenient = true;
            try
            {
                reader.Position = start;
                int limit = reader.Length;
                (int read, int skipped) = ReadLeniently(fields, ref reader, instance, resolver, limit);

                if (read == 0)
                {
                    Interlocked.Increment(ref s_failed);
                    LogOnce($"none:{instance.ClassName}:{instance.PathID}",
                        $"[TypeTreeRead] '{instance.ClassName}' (PathID {instance.PathID}) has no readable " +
                        $"field ({original.GetType().Name}: {original.Message})");
                    // 回退：让上游照常抛它自己的异常并给出原来的日志。
                    reader.Position = start;
                    return true;
                }

                // 走到末尾：剩下的字节是旧类型树解释不了的残留，
                // 继续争它只会把整条资源判成 UnreadableObject。
                reader.Position = limit;

                Interlocked.Increment(ref s_recovered);
                LogOnce($"ok:{instance.ClassName}:{instance.PathID}",
                    $"[TypeTreeRead] '{instance.ClassName}' (PathID {instance.PathID}) recovered " +
                    $"{read} field(s), {skipped} skipped, after {original.GetType().Name}: {original.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref s_failed);
                LogOnce($"fail:{instance.ClassName}:{instance.PathID}",
                    $"[TypeTreeRead] '{instance.ClassName}' (PathID {instance.PathID}) lenient read also " +
                    $"failed: {ex.GetType().Name}: {ex.Message}");
                reader.Position = start;
                return true;
            }
            finally
            {
                t_lenient = false;
            }
        }
    }

    /// <summary>
    /// Read fields one at a time, keeping every one that fits.
    ///
    /// <para>A field that overruns is left at its default and reading resumes one byte
    /// past its own start, so a single bad string cannot shift every following field
    /// by its whole length. The walk stops when the buffer is exhausted, and each step
    /// advances by at least one byte, so it always terminates.</para>
    /// </summary>
    private static (int Read, int Skipped) ReadLeniently(SerializableStructure structure,
        ref EndianSpanReader reader, TypeTreeObject owner, ITypeResolver? resolver, int limit)
    {
        SerializableValue[] fields = structure.Fields;
        int read = 0;
        int skipped = 0;
        for (int i = 0; i < fields.Length && reader.Position < limit; i++)
        {
            SerializableType.Field etalon = structure.Type.Fields[i];
            int before = reader.Position;
            try
            {
                fields[i].Read(ref reader, owner.Collection.Version, owner.Collection.Flags,
                    structure.Depth, etalon, resolver!);
                if (reader.Position > before)
                {
                    read++;
                }
                else
                {
                    reader.Position = Math.Min(before + 1, limit);
                    skipped++;
                }
            }
            catch (Exception)
            {
                reader.Position = Math.Min(before + 1, limit);
                skipped++;
            }
        }
        return (read, skipped);
    }

    /// <summary>
    /// The resolver the concrete TypeTree object was built with.
    ///
    /// <para><c>TypeTreeResolver</c> is internal to AssetRipper, so its instance cannot
    /// be created here; but one is already stored on the object that owns these
    /// fields, and the retry has to use the same one or a complex field would be
    /// resolved against a different tree.</para>
    /// </summary>
    private static ITypeResolver? FindResolver(TypeTreeObject owner)
    {
        Type type = owner.GetType();
        lock (ResolverFields)
        {
            if (!ResolverFields.TryGetValue(type, out FieldInfo? field))
            {
                field = type
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(f => typeof(ITypeResolver).IsAssignableFrom(f.FieldType));
                ResolverFields[type] = field;
            }
            return field?.GetValue(owner) as ITypeResolver;
        }
    }

    private static void LogOnce(string key, string message)
    {
        lock (LoggedFailuresLock)
        {
            if (!LoggedFailures.Add(key))
            {
                return;
            }
        }
        Logger.Warning(LogCategory.Import, message);
    }
}