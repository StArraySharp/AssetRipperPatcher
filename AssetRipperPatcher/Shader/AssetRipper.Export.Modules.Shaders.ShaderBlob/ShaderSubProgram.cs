using System;
using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob;

public sealed class ShaderSubProgram
{
	public int ProgramType { get; set; }

	public int StatsALU { get; set; }

	public int StatsTEX { get; set; }

	public int StatsFlow { get; set; }

	public int StatsTempRegister { get; set; }

	public string[] GlobalKeywords { get; set; } = Array.Empty<string>();

	public string[] LocalKeywords { get; set; } = Array.Empty<string>();

	public byte[] ProgramData { get; set; } = Array.Empty<byte>();

	public VectorParameter[] VectorParameters { get; set; } = Array.Empty<VectorParameter>();

	public MatrixParameter[] MatrixParameters { get; set; } = Array.Empty<MatrixParameter>();

	public TextureParameter[] TextureParameters { get; set; } = Array.Empty<TextureParameter>();

	public BufferBinding[] BufferParameters { get; set; } = Array.Empty<BufferBinding>();

	public UAVParameter[] UAVParameters { get; set; } = Array.Empty<UAVParameter>();

	public SamplerParameter[] SamplerParameters { get; set; } = Array.Empty<SamplerParameter>();

	public ConstantBuffer[] ConstantBuffers { get; set; } = Array.Empty<ConstantBuffer>();

	public BufferBinding[] ConstantBufferBindings { get; set; } = Array.Empty<BufferBinding>();

	public StructParameter[] StructParameters { get; set; } = Array.Empty<StructParameter>();

	public ParserBindChannels BindChannels { get; set; } = new ParserBindChannels();

	public static bool HasLocalKeywords(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2019);
	}

	public static bool HasUAVParameters(UnityVersion version)
	{
		return version.GreaterThanOrEquals(5, 5);
	}

	public static bool HasSamplerParameters(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2017, 1);
	}

	public static bool HasMultiSampled(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2017, 3);
	}

	private static bool HasStatsTempRegister(UnityVersion version)
	{
		return version.GreaterThanOrEquals(5, 5);
	}

	private static bool IsAllParamArgs(UnityVersion version)
	{
		return version.GreaterThanOrEquals(5, 5);
	}

	private static bool HasStructParameters(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2017, 3);
	}

	private static bool HasNewTextureParams(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2018, 2);
	}

	public static bool HasMergedKeywords(UnityVersion version)
	{
		return version.GreaterThanOrEquals(2021, 2);
	}

	private static int GetExpectedProgramVersion(UnityVersion version)
	{
		UnityVersion unityVersion = version;
		if (1 == 0)
		{
		}
		int result = ((!version.Equals(5, 3)) ? ((!version.Equals(5, 4)) ? ((!version.Equals(5, 5)) ? ((!version.LessThan(2017, 3)) ? ((!version.LessThan(2018, 2)) ? ((!version.LessThan(2019)) ? ((!version.LessThan(2021, 2)) ? 202012090 : 201806140) : 201802150) : 201708220) : 201609010) : 201608170) : 201510240) : 201509030);
		if (1 == 0)
		{
		}
		return result;
	}

	public void Read(AssetReader reader, bool readProgramData, bool readParams)
	{
		UnityVersion unityVersion = reader.AssetCollection.Version;
		int version = reader.ReadInt32();
		if (version != GetExpectedProgramVersion(unityVersion))
		{
			throw new Exception($"Shader program version {version} doesn't match");
		}
		if (readProgramData)
		{
			ReadProgramData(reader);
		}
		if (readParams)
		{
			ReadParameters(reader);
		}
	}

	private void ReadProgramData(AssetReader reader)
	{
		UnityVersion unityVersion = reader.AssetCollection.Version;
		ProgramType = reader.ReadInt32();
		StatsALU = reader.ReadInt32();
		StatsTEX = reader.ReadInt32();
		StatsFlow = reader.ReadInt32();
		if (HasStatsTempRegister(unityVersion))
		{
			StatsTempRegister = reader.ReadInt32();
		}
		if (HasMergedKeywords(unityVersion))
		{
			reader.ReadStringArray();
		}
		else
		{
			GlobalKeywords = reader.ReadStringArray();
			if (HasLocalKeywords(unityVersion))
			{
				LocalKeywords = reader.ReadStringArray();
			}
		}
		ProgramData = reader.ReadByteArray();
		reader.AlignStream();
		int sourceMap = reader.ReadInt32();
		int bindCount = reader.ReadInt32();
		ShaderBindChannel[] channels = new ShaderBindChannel[bindCount];
		for (int i = 0; i < bindCount; i = checked(i + 1))
		{
			uint source = reader.ReadUInt32();
			VertexComponent target = (VertexComponent)reader.ReadUInt32();
			ShaderBindChannel channel = new ShaderBindChannel(source, target);
			channels[i] = channel;
			sourceMap |= 1 << checked((int)source);
		}
		BindChannels = new ParserBindChannels(channels, sourceMap);
	}

	private void ReadParameters(AssetReader reader)
	{
		UnityVersion unityVersion = reader.AssetCollection.Version;
		List<VectorParameter> vectors = new List<VectorParameter>();
		List<MatrixParameter> matrices = new List<MatrixParameter>();
		List<TextureParameter> textures = new List<TextureParameter>();
		List<VectorParameter> structVectors = new List<VectorParameter>();
		List<MatrixParameter> structMatrices = new List<MatrixParameter>();
		List<BufferBinding> buffers = new List<BufferBinding>();
		List<UAVParameter> uavs = (HasUAVParameters(unityVersion) ? new List<UAVParameter>() : null);
		List<SamplerParameter> samplers = (HasSamplerParameters(unityVersion) ? new List<SamplerParameter>() : null);
		List<BufferBinding> constBindings = new List<BufferBinding>();
		List<StructParameter> structs = new List<StructParameter>();
		int paramGroupCount = reader.ReadInt32();
		checked
		{
			ConstantBuffers = new ConstantBuffer[paramGroupCount - 1];
			for (int i = 0; i < paramGroupCount; i++)
			{
				vectors.Clear();
				matrices.Clear();
				structs.Clear();
				string name = reader.ReadString();
				int usedSize = reader.ReadInt32();
				int paramCount = reader.ReadInt32();
				for (int j = 0; j < paramCount; j++)
				{
					string paramName = reader.ReadString();
					ShaderParamType paramType = unchecked((ShaderParamType)reader.ReadInt32());
					int rows = reader.ReadInt32();
					int columns = reader.ReadInt32();
					bool isMatrix = reader.ReadInt32() > 0;
					int arraySize = reader.ReadInt32();
					int index = reader.ReadInt32();
					if (isMatrix)
					{
						MatrixParameter matrix = (IsAllParamArgs(unityVersion) ? new MatrixParameter(paramName, paramType, index, arraySize, rows, columns) : new MatrixParameter(paramName, paramType, index, rows, columns));
						matrices.Add(matrix);
					}
					else
					{
						VectorParameter vector = (IsAllParamArgs(unityVersion) ? new VectorParameter(paramName, paramType, index, arraySize, columns) : new VectorParameter(paramName, paramType, index, columns));
						vectors.Add(vector);
					}
				}
				if (HasStructParameters(unityVersion))
				{
					int structCount = reader.ReadInt32();
					for (int k = 0; k < structCount; k++)
					{
						structVectors.Clear();
						structMatrices.Clear();
						string structName = reader.ReadString();
						int index2 = reader.ReadInt32();
						int arraySize2 = reader.ReadInt32();
						int structSize = reader.ReadInt32();
						int strucParamCount = reader.ReadInt32();
						for (int l = 0; l < strucParamCount; l++)
						{
							string paramName2 = reader.ReadString();
							paramName2 = structName + "." + paramName2;
							ShaderParamType paramType2 = unchecked((ShaderParamType)reader.ReadInt32());
							int rows2 = reader.ReadInt32();
							int columns2 = reader.ReadInt32();
							bool isMatrix2 = reader.ReadInt32() > 0;
							int vectorArraySize = reader.ReadInt32();
							int paramIndex = reader.ReadInt32();
							if (isMatrix2)
							{
								MatrixParameter matrix2 = (IsAllParamArgs(unityVersion) ? new MatrixParameter(paramName2, paramType2, paramIndex, vectorArraySize, rows2, columns2) : new MatrixParameter(paramName2, paramType2, paramIndex, rows2, columns2));
								structMatrices.Add(matrix2);
							}
							else
							{
								VectorParameter vector2 = (IsAllParamArgs(unityVersion) ? new VectorParameter(paramName2, paramType2, paramIndex, vectorArraySize, columns2) : new VectorParameter(paramName2, paramType2, paramIndex, columns2));
								structVectors.Add(vector2);
							}
						}
						StructParameter @struct = new StructParameter(structName, index2, arraySize2, structSize, structVectors.ToArray(), structMatrices.ToArray());
						structs.Add(@struct);
					}
				}
				if (i == 0)
				{
					VectorParameters = vectors.ToArray();
					MatrixParameters = matrices.ToArray();
					StructParameters = structs.ToArray();
				}
				else
				{
					ConstantBuffer constBuffer = new ConstantBuffer(name, matrices.ToArray(), vectors.ToArray(), structs.ToArray(), usedSize);
					ConstantBuffers[i - 1] = constBuffer;
				}
			}
			int paramGroup2Count = reader.ReadInt32();
			for (int m = 0; m < paramGroup2Count; m++)
			{
				string name2 = reader.ReadString();
				int type = reader.ReadInt32();
				int index3 = reader.ReadInt32();
				int extraValue = reader.ReadInt32();
				switch (type)
				{
				case 0:
				{
					TextureParameter texture;
					if (HasNewTextureParams(unityVersion))
					{
						uint textureExtraValue = reader.ReadUInt32();
						bool isMultiSampled = (textureExtraValue & 1) == 1;
						byte dimension = (byte)(textureExtraValue >> 1);
						int samplerIndex = extraValue;
						texture = new TextureParameter(name2, index3, dimension, samplerIndex, isMultiSampled);
					}
					else if (HasMultiSampled(unityVersion))
					{
						uint textureExtraValue2 = reader.ReadUInt32();
						bool isMultiSampled2 = textureExtraValue2 == 1;
						byte dimension2 = unchecked((byte)extraValue);
						int samplerIndex2 = extraValue >> 8;
						if (samplerIndex2 == 16777215)
						{
							samplerIndex2 = -1;
						}
						texture = new TextureParameter(name2, index3, dimension2, samplerIndex2, isMultiSampled2);
					}
					else
					{
						byte dimension3 = unchecked((byte)extraValue);
						int samplerIndex3 = extraValue >> 8;
						if (samplerIndex3 == 16777215)
						{
							samplerIndex3 = -1;
						}
						texture = new TextureParameter(name2, index3, dimension3, samplerIndex3);
					}
					textures.Add(texture);
					continue;
				}
				case 1:
				{
					BufferBinding binding = new BufferBinding(name2, index3);
					constBindings.Add(binding);
					continue;
				}
				case 2:
				{
					BufferBinding buffer = new BufferBinding(name2, index3);
					buffers.Add(buffer);
					continue;
				}
				case 3:
					if (uavs != null)
					{
						UAVParameter uav = new UAVParameter(name2, index3, extraValue);
						uavs.Add(uav);
						continue;
					}
					break;
				}
				if (type == 4 && samplers != null)
				{
					SamplerParameter sampler = new SamplerParameter((uint)extraValue, index3);
					samplers.Add(sampler);
					continue;
				}
				throw new Exception($"Unupported parameter type {type}");
			}
			TextureParameters = textures.ToArray();
			BufferParameters = buffers.ToArray();
			if (uavs != null)
			{
				UAVParameters = uavs.ToArray();
			}
			if (samplers != null)
			{
				SamplerParameters = samplers.ToArray();
			}
			ConstantBufferBindings = constBindings.ToArray();
			if (HasStructParameters(unityVersion))
			{
				StructParameters = structs.ToArray();
			}
		}
	}

	public ShaderGpuProgramType GetProgramType(UnityVersion version)
	{
		if (ShaderGpuProgramTypeExtensions.GpuProgramType55Relevant(version))
		{
			return ((ShaderGpuProgramType55)ProgramType).ToGpuProgramType();
		}
		return ((ShaderGpuProgramType53)ProgramType).ToGpuProgramType();
	}
}
