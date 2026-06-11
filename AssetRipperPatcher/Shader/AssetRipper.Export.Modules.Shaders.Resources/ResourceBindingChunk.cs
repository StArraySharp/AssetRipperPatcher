#define DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.IO.Endian;

namespace AssetRipper.Export.Modules.Shaders.Resources;

internal class ResourceBindingChunk
{
	private readonly Dictionary<string, uint> m_nameLookup;

	private readonly List<Sampler> m_Samplers;

	private ShaderSubProgram m_shaderSubprogram;

	internal uint Count => checked((uint)m_shaderSubprogram.ConstantBuffers.Length + (uint)m_shaderSubprogram.TextureParameters.Length + (uint)m_Samplers.Count + (uint)m_shaderSubprogram.BufferParameters.Length);

	internal uint Size { get; }

	public ResourceBindingChunk(ref ShaderSubProgram shaderSubprogram, uint resourceBindingOffset, Dictionary<string, uint> nameLookup)
	{
		m_shaderSubprogram = shaderSubprogram;
		m_nameLookup = nameLookup;
		m_Samplers = CreateSamplers(ref shaderSubprogram);
		checked
		{
			uint nameOffset = resourceBindingOffset + 32 * Count;
			BufferBinding[] bufferParameters = shaderSubprogram.BufferParameters;
			foreach (BufferBinding bufferParam in bufferParameters)
			{
				nameLookup[bufferParam.Name] = nameOffset;
				nameOffset += (uint)bufferParam.Name.Length + 1;
			}
			TextureParameter[] textureParameters = shaderSubprogram.TextureParameters;
			foreach (TextureParameter textureParam in textureParameters)
			{
				nameLookup[textureParam.Name] = nameOffset;
				nameOffset += (uint)textureParam.Name.Length + 1;
			}
			foreach (Sampler sampler in m_Samplers)
			{
				nameLookup[sampler.Name] = nameOffset;
				nameOffset += (uint)sampler.Name.Length + 1;
			}
			BufferBinding[] constantBufferBindings = shaderSubprogram.ConstantBufferBindings;
			foreach (BufferBinding constantBuffer in constantBufferBindings)
			{
				nameLookup[constantBuffer.Name] = nameOffset;
				nameOffset += (uint)constantBuffer.Name.Length + 1;
			}
			Size = nameOffset - resourceBindingOffset;
		}
	}

	private static List<Sampler> CreateSamplers(ref ShaderSubProgram shaderSubprogram)
	{
		List<Sampler> samplers = new List<Sampler>();
		TextureParameter[] textureParameters = shaderSubprogram.TextureParameters;
		foreach (TextureParameter textureParam in textureParameters)
		{
			if (textureParam.SamplerIndex >= 0 && textureParam.SamplerIndex != 65535)
			{
				string samplerName = "sampler" + textureParam.Name;
				samplers.Add(new Sampler(samplerName, checked((uint)textureParam.SamplerIndex), IsComparisonSampler: false));
			}
		}
		SamplerParameter[] samplerParameters = shaderSubprogram.SamplerParameters;
		foreach (SamplerParameter samplerParam in samplerParameters)
		{
			SamplerFilterMode filterMode = (SamplerFilterMode)checked((int)(samplerParam.Sampler & 3));
			SamplerWrapMode wrapU = (SamplerWrapMode)checked((int)((samplerParam.Sampler >> 2) & 3));
			SamplerWrapMode wrapV = (SamplerWrapMode)checked((int)((samplerParam.Sampler >> 4) & 3));
			SamplerWrapMode wrapW = (SamplerWrapMode)checked((int)((samplerParam.Sampler >> 6) & 3));
			bool isComparisonSampler = (samplerParam.Sampler & 0x100) != 0;
			string samplerName2 = ((wrapU != wrapV || wrapU != wrapW) ? $"{filterMode}_{wrapU}U_{wrapV}V_{wrapW}W" : $"{filterMode}_{wrapU}");
			if (isComparisonSampler)
			{
				samplerName2 += "_Comparison";
			}
			samplerName2 += $"_Sampler{samplerParam.BindPoint}";
			samplers.Add(new Sampler(samplerName2, checked((uint)samplerParam.BindPoint), isComparisonSampler));
		}
		samplers = samplers.OrderBy((Sampler s) => s.BindPoint).ToList();
		Debug.Assert(samplers.Select((Sampler s) => s.BindPoint).Distinct().Count() == samplers.Select((Sampler s) => s.BindPoint).Count(), "Duplicate sampler bindpoint");
		Debug.Assert(samplers.Select((Sampler s) => s.BindPoint).Distinct().Count() == samplers.Select((Sampler s) => s.BindPoint).Count(), "Duplicate sampler name");
		return samplers;
	}

	internal void Write(EndianWriter writer)
	{
		BufferBinding[] bufferParameters = m_shaderSubprogram.BufferParameters;
		checked
		{
			foreach (BufferBinding bufferParam in bufferParameters)
			{
				writer.Write(m_nameLookup[bufferParam.Name]);
				writer.Write(5u);
				writer.Write(6u);
				writer.Write(1u);
				writer.Write(56u);
				writer.Write((uint)bufferParam.Index);
				writer.Write(1u);
				writer.Write(0u);
			}
			foreach (Sampler sampler in m_Samplers)
			{
				writer.Write(m_nameLookup[sampler.Name]);
				writer.Write(3u);
				writer.Write(0u);
				writer.Write(0u);
				writer.Write(0u);
				writer.Write(sampler.BindPoint);
				writer.Write(1u);
				ShaderInputFlags samplerFlags = (sampler.IsComparisonSampler ? ShaderInputFlags.ComparisonSampler : ShaderInputFlags.None);
				writer.Write((uint)samplerFlags);
			}
			TextureParameter[] textureParameters = m_shaderSubprogram.TextureParameters;
			foreach (TextureParameter textureParam in textureParameters)
			{
				writer.Write(m_nameLookup[textureParam.Name]);
				writer.Write(2u);
				writer.Write(5u);
				writer.Write((uint)GetTextureDimension(textureParam));
				writer.Write(uint.MaxValue);
				writer.Write((uint)textureParam.Index);
				writer.Write(1u);
				writer.Write(12u);
			}
			BufferBinding[] constantBufferBindings = m_shaderSubprogram.ConstantBufferBindings;
			foreach (BufferBinding constantBuffer in constantBufferBindings)
			{
				writer.Write(m_nameLookup[constantBuffer.Name]);
				writer.Write(0u);
				writer.Write(0u);
				writer.Write(0u);
				writer.Write(0u);
				writer.Write((uint)constantBuffer.Index);
				writer.Write(1u);
				writer.Write(0u);
			}
			BufferBinding[] bufferParameters2 = m_shaderSubprogram.BufferParameters;
			foreach (BufferBinding bufferParam2 in bufferParameters2)
			{
				writer.WriteStringZeroTerm(bufferParam2.Name);
			}
			TextureParameter[] textureParameters2 = m_shaderSubprogram.TextureParameters;
			foreach (TextureParameter textureParam2 in textureParameters2)
			{
				writer.WriteStringZeroTerm(textureParam2.Name);
			}
			foreach (Sampler sampler2 in m_Samplers)
			{
				writer.WriteStringZeroTerm(sampler2.Name);
			}
			BufferBinding[] constantBufferBindings2 = m_shaderSubprogram.ConstantBufferBindings;
			foreach (BufferBinding constantBuffer2 in constantBufferBindings2)
			{
				writer.WriteStringZeroTerm(constantBuffer2.Name);
			}
		}
	}

	private ShaderResourceViewDimension GetTextureDimension(TextureParameter param)
	{
		return param.Dim switch
		{
			2 => ShaderResourceViewDimension.Texture2D, 
			3 => ShaderResourceViewDimension.Texture3D, 
			4 => ShaderResourceViewDimension.TextureCube, 
			5 => ShaderResourceViewDimension.Texture2DArray, 
			6 => ShaderResourceViewDimension.TextureCubeArray, 
			_ => ShaderResourceViewDimension.Texture2D, 
		};
	}
}
