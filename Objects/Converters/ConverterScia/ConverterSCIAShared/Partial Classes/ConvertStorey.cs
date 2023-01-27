using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel.StructuralElements;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---

        public StructuralStorey StoreyToNative(Structural.Geometry.Storey speckleStorey)
        {
            if (ExistsInContext(speckleStorey, out IEnumerable<StructuralStorey> contextObjects))
                return contextObjects.FirstOrDefault();

            StructuralStorey storeyNative = new StructuralStorey(GetSCIAId(speckleStorey), speckleStorey.name, UnitsNet.Length.FromMeters(speckleStorey.elevation));

            AddToAnalysisModel(storeyNative, speckleStorey);

            return storeyNative;
        }
        #endregion


        #region --- TO SPECKLE ---

        public Structural.Geometry.Storey StoreyToSpeckle(StructuralStorey admStorey)
        {
            if (ExistsInContext(admStorey, out Structural.Geometry.Storey contextObject))
                return contextObject;

            var speckleStorey = new Structural.Geometry.Storey(admStorey.Name, admStorey.HeightLevel.Meters);
            AddToSpeckleModel(speckleStorey, admStorey);

            return speckleStorey;
        }

        #endregion
    }
}