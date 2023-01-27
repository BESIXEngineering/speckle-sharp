using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Subtypes;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects.Structural.Materials;
using Speckle.Core.Models;
using Speckle.Core.Kits;
using ModelExchanger.AnalysisDataModel.Interfaces;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---

        Constraint<UnitsNet.RotationalStiffness?> FixedRotation => new Constraint<UnitsNet.RotationalStiffness?>(ConstraintType.Rigid, null);  // UnitsNet.RotationalStiffness.FromKilonewtonMetersPerRadian(1e+10)
        Constraint<UnitsNet.RotationalStiffness?> FreeRotation => new Constraint<UnitsNet.RotationalStiffness?>(ConstraintType.Free, UnitsNet.RotationalStiffness.FromKilonewtonMetersPerRadian(0));
        Constraint<UnitsNet.ForcePerLength?> FixedTranslation => new Constraint<UnitsNet.ForcePerLength?>(ConstraintType.Rigid, null);  // UnitsNet.ForcePerLength.FromKilonewtonsPerMeter(1e+10)
        Constraint<UnitsNet.ForcePerLength?> FreeTranslation => new Constraint<UnitsNet.ForcePerLength?>(ConstraintType.Free, UnitsNet.ForcePerLength.FromKilonewtonsPerMeter(0));
        Constraint<UnitsNet.Pressure?> FixedTranslationLine => new Constraint<UnitsNet.Pressure?>(ConstraintType.Rigid, null);  // UnitsNet.Pressure.FromKilopascals(1e+10)
        Constraint<UnitsNet.RotationalStiffnessPerLength?> FreeRotationLine => new Constraint<UnitsNet.RotationalStiffnessPerLength?>(ConstraintType.Free, UnitsNet.RotationalStiffnessPerLength.FromKilonewtonMetersPerRadianPerMeter(0));

        string SpeckleRestraintCodeFree => "RRRRRR";
        string SpeckleRestraintCodeHinged => "FFFRRR";
        string SpeckleRestraintCodeFixed => "FFFFFF";


        public void SetADMRestraint<T>(T admObject, string code)
            where T : IAnalysisObject, IHasTranslationConstraintsBase<Constraint<UnitsNet.ForcePerLength?>>, IHasRotationalConstraintsBase<Constraint<UnitsNet.RotationalStiffness?>>
        {
            if (code.Any(c => c != 'F' && c != 'R'))
            {
                throw new NotImplementedException($"Current restraint support limited to free or fixed: {code}");
            }

            admObject.TranslationX = TranslationRestraintCodeToNative(code[0]);
            admObject.TranslationY = TranslationRestraintCodeToNative(code[1]);
            admObject.TranslationZ = TranslationRestraintCodeToNative(code[2]);
            admObject.RotationX = RotationRestraintCodeToNative(code[3]);
            admObject.RotationY = RotationRestraintCodeToNative(code[4]);
            admObject.RotationZ = RotationRestraintCodeToNative(code[5]);
        }

        public Constraint<UnitsNet.ForcePerLength?> TranslationRestraintCodeToNative(char character) => character == 'F' ? FixedTranslation : FreeTranslation;
        public Constraint<UnitsNet.RotationalStiffness?> RotationRestraintCodeToNative(char character) => character == 'F' ? FixedRotation : FreeRotation;

        #endregion


        #region --- TO SPECKLE ---

        public Structural.Geometry.Restraint ConstraintToSpeckle<T>(T admObject)
            where T : IAnalysisObject, IHasTranslationConstraintsBase<Constraint<UnitsNet.ForcePerLength?>>, IHasRotationalConstraintsBase<Constraint<UnitsNet.RotationalStiffness?>>
        {
            string code = string.Empty;

            code += ConstraintToSpeckle(admObject.TranslationX, out double stiffnessX);
            code += ConstraintToSpeckle(admObject.TranslationY, out double stiffnessY);
            code += ConstraintToSpeckle(admObject.TranslationZ, out double stiffnessZ);
            code += ConstraintToSpeckle(admObject.RotationX, out double stiffnessXX);
            code += ConstraintToSpeckle(admObject.RotationY, out double stiffnessYY);
            code += ConstraintToSpeckle(admObject.RotationZ, out double stiffnessZZ);

            var restraint = new Structural.Geometry.Restraint(code, stiffnessX, stiffnessY, stiffnessZ, stiffnessXX, stiffnessYY, stiffnessZZ);
            restraint[SpeckleDynamicPropertyName_Name] = admObject.Name;

            AddToSpeckleModel(restraint, admObject);

            return restraint;
        }

        /// <summary>
        /// Convert an ADM constraint to a Speckle restraint code.
        /// Speckle restraint code convention: F = Fixed, R = Released, K = Stiffness
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="admConstraint"></param>
        /// <returns></returns>
        public string ConstraintToSpeckle<T>(Constraint<T> admConstraint, out double stiffness)
        {
            stiffness = 0;
            switch (admConstraint.Type)
            {
                case ConstraintType.Free:
                    return "R";
                case ConstraintType.Rigid:
                    return "F";
                case ConstraintType.Flexible:
                    // todo set unit according to settings
                    if (admConstraint.Stiffness is UnitsNet.ForcePerLength f)
                    {
                        stiffness = f.KilonewtonsPerMeter;
                    }
                    else if (admConstraint.Stiffness is UnitsNet.RotationalStiffness r)
                    {
                        stiffness = r.KilonewtonMetersPerRadian;
                    }
                    else if (admConstraint.Stiffness != null)
                    {
                        throw new NotImplementedException($"Can't convert stiffness of type '{admConstraint.Stiffness.GetType()}'");
                    }
                    return "K";
                default:
                    throw new NotImplementedException("No support for specified constraint");
            }
        }
        #endregion
    }
}
