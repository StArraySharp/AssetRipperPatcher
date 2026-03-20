// AssetRipper.Export.UnityProjects, Version=1.2.4.0, Culture=neutral, PublicKeyToken=null
// AssetRipper.Export.UnityProjects.TextMeshProMapping
using System.Collections.Generic;
using AssetRipper.Primitives;

public class GuidMapping
{
    public static readonly Dictionary<long, UnityGuid> FileIDAndGuid;

    public const string Version = "3.0.9";

    static GuidMapping()
    {
        FileIDAndGuid = new Dictionary<long, UnityGuid>
        {
            {
                1148083418L,
                UnityGuid.Parse("7b743370ac3e4ec2a1668f5455a8ef8a")
            },
            {
                -1620774994L,
                UnityGuid.Parse("2da0c512f12947e489f739169773d7ca")
            },
            {
                -1385168320L,
                UnityGuid.Parse("5143f58107604835ab1a5efa2d8818fd")
            },
            {
                -806885394L,
                UnityGuid.Parse("9541d86e2fd84c1d9990edf0852d74ab")
            },
            {
                1453722849L,
                UnityGuid.Parse("f4688fdb7df04437aeb418b961361dc5")
            },
            {
                -667331979L,
                UnityGuid.Parse("71c1514a6bd24e1e882cebbe1904ce04")
            },
            {
                -1936749209L,
                UnityGuid.Parse("ab2114bdc8544297b417dfefe9f1e410")
            },
            {
                2019389346L,
                UnityGuid.Parse("84a92b25f83d49b9bc132d206b370281")
            },
            {
                -395462249L,
                UnityGuid.Parse("2705215ac5b84b70bacc50632be6e391")
            },
            {
                2108210716L,
                UnityGuid.Parse("54d21f6ece3b46479f0c328f8c6007e0")
            },
            {
                1330537494L,
                UnityGuid.Parse("07994bfe8b0e4adb97d706de5dea48d5")
            },
            {
                1908110080L,
                UnityGuid.Parse("07994bfe8b0e4adb97d706de5dea48d5")
            },
            {
                -907550668L,
                UnityGuid.Parse("6366ee97f6b541449155028b9487355a")
            }
        };
    }
}