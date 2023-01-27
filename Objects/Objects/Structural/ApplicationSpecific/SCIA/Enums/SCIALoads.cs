using System;
using System.Collections.Generic;
using System.Text;

namespace Objects.Structural.SCIA
{
    #region LOAD GROUPS
    public enum SCIALoadGroupType
    {
        //Permanent load case
        Permanent = 0,
        //Variable  load case
        Variable = 1,
        Seismic = 3,
        Moving = 100,
        Tensioning = 101,
        Fire = 102,
        // Accidental load case
        Accidental = 2,
    }

    public enum SCIALoadGroupRelation
    {
        Standard = 0, // Standard, the load cases can be sorted but it does not affect the process of combination generation
        Exclusive = 1,  // Exclusive, two load cases from the same load group will never appear in the same combination, 
        Together = 2,  // Together, all load cases in the same load group are always inserted into every new combination
        NotDefined = -1, // Case null
    }

    public enum SCIAVariableLoadType
    {
        Domestic,
        Offices,
        Congregation,
        Shopping,
        Storage,
        VehicleSmallerThan30kN,
        VehicleLargerThan30kN,
        Roofs,
        Snow,
        Wind,
        Temperature,
        ConstructionLoads,
        Other,
        NotDefined = -1, // Case null
    }
    #endregion


    #region LOAD COMBINATIONS
    public enum SCIALoadCombinationCategory
    {
        UltimateLimitState,
        ServiceabilityLimitState,
        AccidentalLimitState,
        AccordingNationalStandard,
        NotDefined
    }

    public enum SCIANationalStandard
    {
        EnUlsSetB = 0,  // EN-ULS (STR/GEO) Set B
        EnUlsSetC = 1,  // EN-ULS (STR/GEO) Set C
        EnAccidental1 = 2,  // EN-Accidental 1
        EnAccidental2 = 3,  // EN-Accidental 2
        EnSeismic = 4,  // EN-Seismic
        EnSlsCharacteristic = 5,  // EN-SLS Characteristic
        EnSlsFrequent = 6,  // EN-SLS Frequent
        EnSlsQuasiPermanent = 7,  // EN-SLS Quasi-permanent
        IbcLrfdUltimate = 8,  // IBC-LRFD ultimate
        IbcAsdUltimate = 9,  // IBC-ASD ultimate
        IbcAsdServiceability = 10,  // IBC-ASD serviceability
        IbcAsdSeismic = 11,  // IBC-ASD seismic
        IbcLrfdSeismic = 12,  // IBC-LRFD seismic
        NotDefined = -1, // Case null
    }
    #endregion
}
