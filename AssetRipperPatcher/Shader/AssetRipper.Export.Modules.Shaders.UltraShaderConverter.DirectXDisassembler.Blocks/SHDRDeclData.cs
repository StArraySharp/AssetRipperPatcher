using System;
using System.IO;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;

public class SHDRDeclData
{
	public int globalFlags;

	public int numTemps;

	public ResourceDimension resourceDimension;

	public ConstantBufferType constantBufferType;

	public int samplerIndex;

	public SamplerMode samplerMode;

	public InterpolationMode interpolation;

	public PrimitiveTopology outputPrimitiveTopology;

	public Primitive inputPrimitive;

	public int maxOutputVertexCount;

	public TessDomain tessDomain;

	public TessPartitioning tessPartitioning;

	public TessOutputPrimitive tessOutPrim;

	public int[]? workGroupSize;

	public int hullPhaseInstanceInfo;

	public float maxTessFactor;

	public int indexRange;

	public float[][] customDataArray = Array.Empty<float[]>();

	public int interfaceId;

	public int interfaceFuncTableCount;

	public int interfaceArraySize;

	public int uavGloballyCoherentAccess;

	public int uavBufferSize;

	public byte uavCounter;

	public ResourceReturnType uavType;

	public string? nameToken;

	public int tgsmStride;

	public int tgsmCount;

	public SHDRInstructionOperand[] operands = Array.Empty<SHDRInstructionOperand>();

	private static readonly string[] nameTokens = new string[23]
	{
		"undefined", "position", "clip_distance", "cull_distance", "rendertarget_array_index", "viewport_array_index", "vertex_id", "primitive_id", "instance_id", "is_front_face",
		"sampleIndex", "finalQuadUeq0EdgeTessFactor", "finalQuadVeq0EdgeTessFactor", "finalQuadUeq1EdgeTessFactor", "finalQuadVeq1EdgeTessFactor", "finalQuadUInsideTessFactor", "finalQuadVInsideTessFactor", "finalTriUeq0EdgeTessFactor", "finalTriVeq0EdgeTessFactor", "finalTriWeq0EdgeTessFactor",
		"finalTriInsideTessFactor", "finalLineDetailTessFactor", "finalLineDensityTessFactor"
	};

	public SHDRDeclData(BinaryReader reader, SHDRInstruction inst, int instData)
	{
		if (!SHDRInstruction.IsDeclaration(inst.opcode))
		{
			return;
		}
		int extraDataStart = checked(SHDRInstruction.GetOperandCount(Opcode.dcl_indexrange) * 4);
		switch (inst.opcode)
		{
		case Opcode.dcl_resource:
			resourceDimension = (ResourceDimension)((instData & 0xF800) >> 11);
			operands = new SHDRInstructionOperand[1]
			{
				new SHDRInstructionOperand(reader)
			};
			break;
		case Opcode.dcl_constantbuffer:
			constantBufferType = (ConstantBufferType)((instData & 0xFFF800) >> 11);
			operands = new SHDRInstructionOperand[1]
			{
				new SHDRInstructionOperand(reader)
			};
			break;
		case Opcode.dcl_sampler:
			checked
			{
				reader.BaseStream.Position += 4L;
				samplerIndex = reader.ReadInt32();
			}
			samplerMode = (SamplerMode)((instData & 0xFFF800) >> 11);
			break;
		case Opcode.dcl_outputtopology:
			outputPrimitiveTopology = (PrimitiveTopology)((instData & 0x1F800) >> 11);
			break;
		case Opcode.dcl_inputprimitive:
			inputPrimitive = (Primitive)((instData & 0x1F800) >> 11);
			break;
		case Opcode.dcl_maxout:
			maxOutputVertexCount = reader.ReadInt32();
			break;
		case Opcode.dcl_tessellator_paritioning:
			tessPartitioning = (TessPartitioning)((instData & 0x3800) >> 11);
			break;
		case Opcode.dcl_tessellator_domain:
			tessDomain = (TessDomain)((instData & 0x1800) >> 11);
			break;
		case Opcode.dcl_tessellator_output_primitive:
			tessOutPrim = (TessOutputPrimitive)((instData & 0x3800) >> 11);
			break;
		case Opcode.dcl_thread_group:
			workGroupSize = new int[3]
			{
				reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadInt32()
			};
			break;
		case Opcode.dcl_input_siv:
			if (inst.shader.shaderType == Type.Pixel)
			{
				interpolation = (InterpolationMode)((instData & 0x7800) >> 11);
			}
			break;
		case Opcode.dcl_input_ps:
			interpolation = (InterpolationMode)((instData & 0x7800) >> 11);
			break;
		case Opcode.dcl_input_sgv:
		case Opcode.dcl_input_ps_sgv:
			checked
			{
				reader.BaseStream.Position += 8L;
				nameToken = nameTokens[reader.ReadInt32()];
				break;
			}
		case Opcode.dcl_input_ps_siv:
			interpolation = (InterpolationMode)((instData & 0x7800) >> 11);
			break;
		case Opcode.dcl_output_siv:
			checked
			{
				reader.BaseStream.Position += 8L;
				nameToken = nameTokens[reader.ReadInt32()];
				break;
			}
		case Opcode.dcl_temps:
			numTemps = reader.ReadInt32();
			break;
		case Opcode.dcl_globalFlags:
			globalFlags = (instData & 0xFFF800) >> 11;
			break;
		case Opcode.dcl_interface:
			interfaceId = reader.ReadInt32();
			checked
			{
				reader.BaseStream.Position += 4L;
				int tableInfo = reader.ReadInt32();
				interfaceFuncTableCount = tableInfo & 0xFFFF;
				interfaceArraySize = (tableInfo & -65536) >> 16;
				break;
			}
		case Opcode.dcl_output_control_point_count:
			maxOutputVertexCount = reader.ReadInt32();
			break;
		case Opcode.dcl_hs_fork_phase_instance_count:
			hullPhaseInstanceInfo = reader.ReadInt32();
			break;
		case Opcode.customdata:
		{
			int customDataOperandCount = checked(reader.ReadInt32() - 2) / 4;
			customDataArray = new float[customDataOperandCount][];
			for (int i = 0; i < customDataOperandCount; i = checked(i + 1))
			{
				customDataArray[i] = new float[4]
				{
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle()
				};
			}
			break;
		}
		case Opcode.dcl_hs_max_tessfactor:
			maxTessFactor = reader.ReadSingle();
			break;
		case Opcode.dcl_uav_typed:
			resourceDimension = (ResourceDimension)((instData & 0xF800) >> 11);
			uavGloballyCoherentAccess = instData & 0x10000;
			uavCounter = 0;
			uavBufferSize = 0;
			checked
			{
				reader.BaseStream.Position += 4L;
			}
			uavType = (ResourceReturnType)(reader.ReadInt32() & 0xF);
			break;
		case Opcode.dcl_uav_raw:
			uavGloballyCoherentAccess = instData & 0x10000;
			uavCounter = 0;
			uavBufferSize = 0;
			break;
		case Opcode.dcl_uav_structured:
			uavGloballyCoherentAccess = instData & 0x10000;
			uavCounter = 0;
			uavBufferSize = 0;
			break;
		case Opcode.dcl_tgsm_structured:
			uavGloballyCoherentAccess = 0;
			checked
			{
				reader.BaseStream.Position += 4L;
				tgsmStride = reader.ReadInt32();
				tgsmCount = reader.ReadInt32();
				break;
			}
		case Opcode.dcl_tgsm_raw:
			uavGloballyCoherentAccess = 0;
			checked
			{
				reader.BaseStream.Position += 4L;
				tgsmStride = 4;
				tgsmCount = reader.ReadInt32();
				break;
			}
		}
	}
}
