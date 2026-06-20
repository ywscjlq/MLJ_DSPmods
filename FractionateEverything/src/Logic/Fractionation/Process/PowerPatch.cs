using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FE.Logic.Fractionation.Fractionators;
using HarmonyLib;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation.Process;

public static partial class ProcessManager {[HarmonyTranspiler]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick))]
    [HarmonyPatch(typeof(GameLogic), nameof(GameLogic._fractionator_parallel))]
    public static IEnumerable<CodeInstruction> FactorySystem_SetPCState_Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase original) {
        MethodInfo targetUpdateMethod =
            AccessTools.Method(typeof(FractionatorComponent), nameof(FractionatorComponent.InternalUpdate));
        MethodInfo replacementUpdateMethod =
            AccessTools.Method(typeof(ProcessManager), nameof(InternalUpdateWithModDispatch));
        List<CodeInstruction> codes = instructions.ToList();
        foreach (CodeInstruction instruction in codes) {
            if (instruction.Calls(targetUpdateMethod)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacementUpdateMethod;
            }
        }

        CodeMatcher matcher = new(codes);
        MethodInfo targetSetPCStateMethod =
            AccessTools.Method(typeof(FractionatorComponent), nameof(FractionatorComponent.SetPCState));
        MethodInfo replacementSetPCStateMethod =
            AccessTools.Method(typeof(ProcessManager), nameof(SetPCStateWithEntityPool));
        FieldInfo factoryField = AccessTools.Field(typeof(FactorySystem), "factory");
        FieldInfo entityPoolField = AccessTools.Field(typeof(PlanetFactory), "entityPool");

        matcher.MatchForward(false, new CodeMatch(i => i.Calls(targetSetPCStateMethod)))
            .Repeat(m => {
                if (original.DeclaringType == typeof(FactorySystem)) {
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0));// FactorySystem
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, factoryField));// PlanetFactory
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Dup));// PlanetFactory, PlanetFactory
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, entityPoolField));// PlanetFactory, EntityData[]
                } else {
                    var locals = original.GetMethodBody()!.LocalVariables;
                    var planetFactoryLocal = locals.First(v => v.LocalType == typeof(PlanetFactory));
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Ldloc_S,
                        (byte)planetFactoryLocal.LocalIndex));// PlanetFactory
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Dup));// PlanetFactory, PlanetFactory
                    m.InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, entityPoolField));// PlanetFactory, EntityData[]
                }
                m.SetInstruction(new CodeInstruction(OpCodes.Call, replacementSetPCStateMethod));
            });

        return matcher.InstructionEnumeration();
    }

    public static void SetPCStateWithEntityPool(ref FractionatorComponent fractionator, PowerConsumerComponent[] pcPool,
        EntityData[] entityPool, PlanetFactory factory) {
        long perfStart = GetFractionatorPerfTimestamp();
        int buildingID = entityPool[fractionator.entityId].protoId;
        if (buildingID < IFE交互塔 || buildingID > IFE精馏塔) {
            try {
                fractionator.SetPCState(pcPool);
                return;
            }
            finally {
                RecordFractionatorPerf(FractionatorPerfSetPcVanilla, buildingID,
                    GetFractionatorPerfElapsed(perfStart));
            }
        }
        try {
            fractionator.SetPCState(pcPool, buildingID, factory.planetId);
        }
        finally {
            RecordFractionatorPerf(FractionatorPerfSetPcFe, buildingID,
                GetFractionatorPerfElapsed(perfStart));
        }
    }

    public static void SetPCState(this ref FractionatorComponent fractionator,
        PowerConsumerComponent[] pcPool, int buildingID, int planetId = -1) {
        double num1 = fractionator.fluidInputCargoCount > 0.0001
            ? fractionator.fluidInputCount / (double)fractionator.fluidInputCargoCount
            : 4.0;
        double num2 = (double)fractionator.fluidInputCargoCount < MaxBeltSpeed
            ? (double)fractionator.fluidInputCargoCount
            : MaxBeltSpeed;
        num2 = num2 * num1 - MaxBeltSpeed;
        if (num2 < 0.0)
            num2 = 0.0;
        double powerRatio = buildingID switch {
            IFE点数聚集塔 => 1.0,
            _ => Cargo.powerTableRatio[fractionator.incLevel] * GetFractionatorEnergyRatio(buildingID)
        };
        ref PowerConsumerComponent pc = ref pcPool[fractionator.pcId];
        long baseWork = GetFractionatorWorkEnergyPerTick(buildingID);
        long baseIdle = GetFractionatorIdleEnergyPerTick(buildingID);
        // 词缀节能：从缓存读取并应用到功率
        if (planetId > 0) {
            float reduction = FE.Logic.Fractionation.Affix.FracAffixManager.GetPowerReduction(planetId, fractionator.id);
            if (reduction > 0.001f) {
                float multiplier = 1f - reduction;
                if (multiplier < 0.1f) multiplier = 0.1f; // 最低10%功率
                baseWork = (long)(baseWork * multiplier);
                baseIdle = (long)(baseIdle * multiplier);
            }
        }
        pc.workEnergyPerTick = baseWork;
        pc.idleEnergyPerTick = baseIdle;
        int permillage = (int)((num2 * 50.0 * 30.0 / MaxBeltSpeed + 1000.0) * powerRatio + 0.5);
        pc.SetRequiredEnergy(fractionator.isWorking, permillage);
    }

    private static long GetFractionatorWorkEnergyPerTick(int buildingID) {
        return buildingID switch {
            IFE交互塔 => InteractionTower.workEnergyPerTick,
            IFE矿物复制塔 => MineralReplicationTower.workEnergyPerTick,
            IFE点数聚集塔 => PointAggregateTower.workEnergyPerTick,
            IFE转化塔 => ConversionTower.workEnergyPerTick,
            IFE精馏塔 => RectificationTower.workEnergyPerTick,
            _ => 0
        };
    }

    private static long GetFractionatorIdleEnergyPerTick(int buildingID) {
        return buildingID switch {
            IFE交互塔 => InteractionTower.idleEnergyPerTick,
            IFE矿物复制塔 => MineralReplicationTower.idleEnergyPerTick,
            IFE点数聚集塔 => PointAggregateTower.idleEnergyPerTick,
            IFE转化塔 => ConversionTower.idleEnergyPerTick,
            IFE精馏塔 => RectificationTower.idleEnergyPerTick,
            _ => 0
        };
    }

    private static float GetFractionatorEnergyRatio(int buildingID) {
        return buildingID switch {
            IFE交互塔 => InteractionTower.EnergyRatio,
            IFE矿物复制塔 => MineralReplicationTower.EnergyRatio,
            IFE转化塔 => ConversionTower.EnergyRatio,
            IFE精馏塔 => RectificationTower.EnergyRatio,
            _ => 1.0f
        };
    }

}
