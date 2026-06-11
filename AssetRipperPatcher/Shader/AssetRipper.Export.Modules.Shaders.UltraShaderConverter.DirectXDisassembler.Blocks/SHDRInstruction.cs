#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;

public class SHDRInstruction
{
	public int instData;

	public int length;

	public bool operandExtended;

	public Opcode opcode;

	public bool saturated;

	public int addrOffsetIU;

	public int addrOffsetIV;

	public int addrOffsetIW;

	public int typeX;

	public int typeY;

	public int typeZ;

	public int typeW;

	public ResourceDimension resDim;

	public int opcodeExtraData;

	public int operandIndex;

	public int operandType;

	public int operandComponents;

	public SHDRDeclData? declData;

	public List<SHDRInstructionOperand> operands;

	public SHDR shader;

	public SHDRInstruction(BinaryReader reader, SHDR shader)
	{
		this.shader = shader;
		long startPos = reader.BaseStream.Position;
		instData = reader.ReadInt32();
		length = (instData & 0x7F000000) >> 24;
		operandExtended = (instData & 0x80000000u) >> 31 == 1;
		opcode = (Opcode)(instData & 0x7FF);
		saturated = (instData & 0x2000) != 0;
		bool extended = operandExtended;
		while (extended)
		{
			int extOpcodeToken = reader.ReadInt32();
			ExtendedOpcodeType extOpcodeType = (ExtendedOpcodeType)(extOpcodeToken & 0x3F);
			extended = (extOpcodeToken & 0x80000000u) >> 31 == 1;
			switch (extOpcodeType)
			{
			case ExtendedOpcodeType.SampleControls:
				addrOffsetIU = (extOpcodeToken & 0x1E00) >> 9;
				addrOffsetIV = (extOpcodeToken & 0x1E000) >> 13;
				addrOffsetIW = (extOpcodeToken & 0x1E0000) >> 17;
				break;
			case ExtendedOpcodeType.ResourceReturnType:
				typeX = (extOpcodeToken & 0x3C0) >> 6;
				typeY = (extOpcodeToken & 0x3C00) >> 10;
				typeZ = (extOpcodeToken & 0x3C000) >> 14;
				typeW = (extOpcodeToken & 0x3C0000) >> 18;
				break;
			case ExtendedOpcodeType.ResourceDim:
				resDim = (ResourceDimension)((extOpcodeToken & 0x7C0) >> 6);
				break;
			}
		}
		opcodeExtraData = (instData & 0xFFF800) >> 11;
		operands = new List<SHDRInstructionOperand>();
		checked
		{
			long endPos = startPos + length * 4;
			if (IsDeclaration(opcode))
			{
				reader.BaseStream.Position = startPos + 4;
				declData = new SHDRDeclData(reader, this, instData);
				reader.BaseStream.Position = startPos + length * 4;
			}
			else
			{
				while (reader.BaseStream.Position < endPos)
				{
					operands.Add(new SHDRInstructionOperand(reader));
				}
			}
			if (reader.BaseStream.Position > endPos && opcode != Opcode.customdata)
			{
				throw new Exception($"went over end pos ({opcode})");
			}
			if (opcode == Opcode.customdata)
			{
				Debug.Assert(declData != null, "declData != null");
				reader.BaseStream.Position = startPos + declData.customDataArray.Length * 16 + 8;
			}
			else
			{
				reader.BaseStream.Position = endPos;
			}
		}
	}

	public static int GetOperandCount(Opcode opcode)
	{
		switch (opcode)
		{
		case Opcode.@break:
		case Opcode.@continue:
		case Opcode.cut:
		case Opcode.@default:
		case Opcode.@else:
		case Opcode.emit:
		case Opcode.emit_then_cut:
		case Opcode.endif:
		case Opcode.endloop:
		case Opcode.endswitch:
		case Opcode.loop:
		case Opcode.customdata:
		case Opcode.nop:
		case Opcode.ret:
		case Opcode.dcl_sampler:
		case Opcode.dcl_outputtopology:
		case Opcode.dcl_inputprimitive:
		case Opcode.dcl_maxout:
		case Opcode.dcl_input_ps_siv:
		case Opcode.dcl_output_sgv:
		case Opcode.dcl_temps:
		case Opcode.dcl_indexableTemp:
		case Opcode.dcl_globalFlags:
		case Opcode.hs_decls:
		case Opcode.hs_control_point_phase:
		case Opcode.hs_fork_phase:
		case Opcode.hs_join_phase:
		case Opcode.dcl_function_table:
		case Opcode.dcl_interface:
		case Opcode.dcl_input_control_point_count:
		case Opcode.dcl_output_control_point_count:
		case Opcode.dcl_tessellator_domain:
		case Opcode.dcl_tessellator_paritioning:
		case Opcode.dcl_tessellator_output_primitive:
		case Opcode.dcl_hs_max_tessfactor:
		case Opcode.dcl_hs_fork_phase_instance_count:
		case Opcode.dcl_thread_group:
		case Opcode.sync:
			return 0;
		case Opcode.@case:
		case Opcode.label:
		case Opcode.@switch:
		case Opcode.dcl_resource:
		case Opcode.dcl_constantbuffer:
		case Opcode.dcl_indexrange:
		case Opcode.dcl_input:
		case Opcode.dcl_input_sgv:
		case Opcode.dcl_input_siv:
		case Opcode.dcl_input_ps:
		case Opcode.dcl_input_ps_sgv:
		case Opcode.dcl_output:
		case Opcode.dcl_output_siv:
		case Opcode.emit_stream:
		case Opcode.cut_stream:
		case Opcode.emit_then_cut_stream:
		case Opcode.fcall:
		case Opcode.dcl_stream:
		case Opcode.dcl_function_body:
		case Opcode.dcl_uav_raw:
		case Opcode.dcl_uav_structured:
		case Opcode.dcl_tgsm_raw:
		case Opcode.dcl_tgsm_structured:
		case Opcode.dcl_resource_raw:
		case Opcode.dcl_resource_structured:
			return 1;
		case Opcode.breakc:
		case Opcode.callc:
		case Opcode.continuec:
		case Opcode.deriv_rtx:
		case Opcode.deriv_rty:
		case Opcode.discard:
		case Opcode.exp:
		case Opcode.frc:
		case Opcode.ftoi:
		case Opcode.ftou:
		case Opcode.@if:
		case Opcode.ineg:
		case Opcode.itof:
		case Opcode.log:
		case Opcode.mov:
		case Opcode.not:
		case Opcode.retc:
		case Opcode.round_ne:
		case Opcode.round_ni:
		case Opcode.round_pi:
		case Opcode.round_z:
		case Opcode.rsq:
		case Opcode.sqrt:
		case Opcode.utof:
		case Opcode.deriv_rtx_coarse:
		case Opcode.deriv_rtx_fine:
		case Opcode.deriv_rty_coarse:
		case Opcode.deriv_rty_fine:
		case Opcode.rcp:
		case Opcode.f32tof16:
		case Opcode.f16tof32:
		case Opcode.countbits:
		case Opcode.firstbit_hi:
		case Opcode.firstbit_lo:
		case Opcode.firstbit_shi:
		case Opcode.bfrev:
		case Opcode.dcl_uav_typed:
		case Opcode.imm_atomic_alloc:
		case Opcode.imm_atomic_consume:
		case Opcode.dmov:
		case Opcode.dtof:
		case Opcode.ftod:
		case Opcode.eval_centroid:
		case Opcode.drcp:
			return 2;
		case Opcode.add:
		case Opcode.and:
		case Opcode.div:
		case Opcode.dp2:
		case Opcode.dp3:
		case Opcode.dp4:
		case Opcode.eq:
		case Opcode.ge:
		case Opcode.iadd:
		case Opcode.ieq:
		case Opcode.ige:
		case Opcode.ilt:
		case Opcode.imax:
		case Opcode.imin:
		case Opcode.imul:
		case Opcode.ine:
		case Opcode.ishl:
		case Opcode.ishr:
		case Opcode.ld:
		case Opcode.lt:
		case Opcode.min:
		case Opcode.max:
		case Opcode.mul:
		case Opcode.ne:
		case Opcode.or:
		case Opcode.sincos:
		case Opcode.ult:
		case Opcode.uge:
		case Opcode.umax:
		case Opcode.umin:
		case Opcode.ushr:
		case Opcode.xor:
		case Opcode.samplepos:
		case Opcode.atomic_and:
		case Opcode.atomic_or:
		case Opcode.atomic_xor:
		case Opcode.atomic_iadd:
		case Opcode.atomic_imax:
		case Opcode.atomic_imin:
		case Opcode.atomic_umax:
		case Opcode.atomic_umin:
		case Opcode.dadd:
		case Opcode.dmax:
		case Opcode.dmin:
		case Opcode.dmul:
		case Opcode.deq:
		case Opcode.dge:
		case Opcode.dlt:
		case Opcode.dne:
		case Opcode.ddiv:
			return 3;
		case Opcode.imad:
		case Opcode.ldms:
		case Opcode.mad:
		case Opcode.movc:
		case Opcode.sample:
		case Opcode.udiv:
		case Opcode.lod:
		case Opcode.gather4:
		case Opcode.ubfe:
		case Opcode.ibfe:
		case Opcode.ld_structured:
		case Opcode.store_structured:
		case Opcode.atomic_cmp_store:
		case Opcode.imm_atomic_iadd:
		case Opcode.imm_atomic_and:
		case Opcode.imm_atomic_or:
		case Opcode.imm_atomic_xor:
		case Opcode.imm_atomic_exch:
		case Opcode.imm_atomic_imax:
		case Opcode.imm_atomic_imin:
		case Opcode.imm_atomic_umax:
		case Opcode.imm_atomic_umin:
		case Opcode.dmovc:
		case Opcode.dfma:
			return 4;
		case Opcode.sample_c:
		case Opcode.sample_c_lz:
		case Opcode.sample_l:
		case Opcode.sample_b:
		case Opcode.gather4_c:
		case Opcode.gather4_po:
		case Opcode.bfi:
		case Opcode.swapc:
		case Opcode.imm_atomic_cmp_exch:
			return 5;
		case Opcode.sample_d:
		case Opcode.gather4_po_c:
			return 6;
		default:
			return -1;
		}
	}

	public static bool IsDeclaration(Opcode opcode)
	{
		switch (opcode)
		{
		case Opcode.customdata:
		case Opcode.dcl_resource:
		case Opcode.dcl_constantbuffer:
		case Opcode.dcl_sampler:
		case Opcode.dcl_indexrange:
		case Opcode.dcl_outputtopology:
		case Opcode.dcl_inputprimitive:
		case Opcode.dcl_maxout:
		case Opcode.dcl_input:
		case Opcode.dcl_input_sgv:
		case Opcode.dcl_input_siv:
		case Opcode.dcl_input_ps:
		case Opcode.dcl_input_ps_sgv:
		case Opcode.dcl_input_ps_siv:
		case Opcode.dcl_output:
		case Opcode.dcl_output_sgv:
		case Opcode.dcl_output_siv:
		case Opcode.dcl_temps:
		case Opcode.dcl_indexableTemp:
		case Opcode.dcl_globalFlags:
		case Opcode.hs_decls:
		case Opcode.hs_control_point_phase:
		case Opcode.hs_fork_phase:
		case Opcode.hs_join_phase:
		case Opcode.dcl_stream:
		case Opcode.dcl_function_body:
		case Opcode.dcl_function_table:
		case Opcode.dcl_interface:
		case Opcode.dcl_input_control_point_count:
		case Opcode.dcl_output_control_point_count:
		case Opcode.dcl_tessellator_domain:
		case Opcode.dcl_tessellator_paritioning:
		case Opcode.dcl_tessellator_output_primitive:
		case Opcode.dcl_hs_max_tessfactor:
		case Opcode.dcl_hs_fork_phase_instance_count:
		case Opcode.dcl_thread_group:
		case Opcode.dcl_uav_typed:
		case Opcode.dcl_uav_raw:
		case Opcode.dcl_uav_structured:
		case Opcode.dcl_tgsm_raw:
		case Opcode.dcl_tgsm_structured:
		case Opcode.dcl_resource_raw:
		case Opcode.dcl_resource_structured:
			return true;
		default:
			return false;
		}
	}
}
