#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.IO.Endian;

namespace AssetRipper.Export.Modules.Shaders.Resources;

internal class ResourceBindingChunk
{
	private readonly Dictionary<string, uint> m_nameLookup;

	private readonly List<Sampler> m_Samplers;

	private readonly List<ConstantBufferBinding> m_constantBufferBindings;

	private ShaderSubProgram m_shaderSubprogram;

	internal uint Count => checked((uint)m_constantBufferBindings.Count + (uint)m_shaderSubprogram.TextureParameters.Length + (uint)m_Samplers.Count + (uint)m_shaderSubprogram.BufferParameters.Length);

	internal uint Size { get; }

	public ResourceBindingChunk(ref ShaderSubProgram shaderSubprogram, uint resourceBindingOffset, Dictionary<string, uint> nameLookup)
	{
		m_shaderSubprogram = shaderSubprogram;
		m_nameLookup = nameLookup;
		m_Samplers = CreateSamplers(ref shaderSubprogram);
		m_constantBufferBindings = CreateConstantBufferBindings(ref shaderSubprogram);
		checked
		{
			uint nameOffset = resourceBindingOffset + 32 * Count;
			BufferBinding[] bufferParameters = shaderSubprogram.BufferParameters;
			foreach (BufferBinding bufferParam in bufferParameters)
			{
				nameLookup[bufferParam.Name] = nameOffset;
				nameOffset += (uint)Encoding.UTF8.GetByteCount(bufferParam.Name) + 1;
			}
			TextureParameter[] textureParameters = shaderSubprogram.TextureParameters;
			foreach (TextureParameter textureParam in textureParameters)
			{
				nameLookup[textureParam.Name] = nameOffset;
				nameOffset += (uint)Encoding.UTF8.GetByteCount(textureParam.Name) + 1;
			}
			foreach (Sampler sampler in m_Samplers)
			{
				nameLookup[sampler.Name] = nameOffset;
				nameOffset += (uint)Encoding.UTF8.GetByteCount(sampler.Name) + 1;
			}
			foreach (ConstantBufferBinding constantBuffer in m_constantBufferBindings)
			{
				nameLookup[constantBuffer.Name] = nameOffset;
				nameOffset += (uint)Encoding.UTF8.GetByteCount(constantBuffer.Name) + 1;
			}
			Size = nameOffset - resourceBindingOffset;
		}
	}

	/// <summary>
	/// One bound-resource entry per constant buffer in the layout table.
	///
	/// <para>Unity's <c>ConstantBuffers</c> and <c>ConstantBufferBindings</c> are
	/// normally the same list, but not always: the layout table is authoritative for
	/// what exists, and a build can omit the binding record for a buffer the runtime
	/// binds by convention.  Writing only <c>ConstantBufferBindings</c> while counting
	/// <c>ConstantBuffers</c> made the RDEF's table size and its written length
	/// disagree, which is exactly the kind of malformed chunk a reader rejects.</para>
	/// </summary>
	private static List<ConstantBufferBinding> CreateConstantBufferBindings(ref ShaderSubProgram shaderSubprogram)
	{
		List<ConstantBufferBinding> bindings = new();
		HashSet<string> seen = new(StringComparer.Ordinal);

		for (int i = 0; i < shaderSubprogram.ConstantBuffers.Length; i++)
		{
			string name = shaderSubprogram.ConstantBuffers[i].Name;
			if (!seen.Add(name))
			{
				continue;
			}

			int index = i;
			foreach (BufferBinding binding in shaderSubprogram.ConstantBufferBindings)
			{
				if (string.Equals(binding.Name, name, StringComparison.Ordinal))
				{
					index = binding.Index;
					break;
				}
			}
			bindings.Add(new ConstantBufferBinding(name, index));
		}

		foreach (BufferBinding binding in shaderSubprogram.ConstantBufferBindings)
		{
			if (!string.IsNullOrEmpty(binding.Name) && seen.Add(binding.Name))
			{
				bindings.Add(new ConstantBufferBinding(binding.Name, binding.Index));
			}
		}

		return bindings;
	}

	private sealed record ConstantBufferBinding(string Name, int Index);

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
			foreach (ConstantBufferBinding constantBuffer in m_constantBufferBindings)
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
			foreach (ConstantBufferBinding constantBuffer2 in m_constantBufferBindings)
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
