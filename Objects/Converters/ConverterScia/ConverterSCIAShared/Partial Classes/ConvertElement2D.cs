using System;
using System.Collections.Generic;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Subtypes;
using ModelExchanger.AnalysisDataModel.StructuralElements;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects.Structural.Geometry;
using Objects.Structural.Properties;
using Speckle.Core.Kits;

namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {

        #region --- TO SCIA ---
        public StructuralSurfaceMember Element2DToNative(Element2D speckleElement)
        {
            if (ExistsInContext(speckleElement, out IEnumerable<StructuralSurfaceMember> contextObjects))
                return contextObjects.FirstOrDefault();

            var admMaterial = MaterialToNative(speckleElement.property.material);
            var thickness = UnitsNet.Length.FromMeters(speckleElement.property.thickness);

            // Convert geometry
            var nodes = speckleElement.topology.Select(n => NodeToNative(n).First() as StructuralPointConnection).ToList();
            var curves = new List<Curve<StructuralPointConnection>>();

            for (int i = 1; i < nodes.Count; i++)
            {
                if (nodes[i - 1].Name == nodes[i].Name) throw new ArgumentException("Duplicate nodes in surface definition");
                var c = new Curve<StructuralPointConnection>(CurveGeometricalShape.Line, new List<StructuralPointConnection> { nodes[i - 1], nodes[i] });
                curves.Add(c);
            }
            // Close the polycurve
            if (nodes[0].Name != nodes.Last().Name)
            {
                var c = new Curve<StructuralPointConnection>(CurveGeometricalShape.Line, new List<StructuralPointConnection> { nodes.Last(), nodes[0] });
                curves.Add(c);
            }

            string name = speckleElement.name ?? GetUniqueADMName<StructuralSurfaceMember>("S");  // string.Format("S{0}", string.Join("", nodes.Select(n => n.Name)));

            StructuralSurfaceMember elementNative = new StructuralSurfaceMember(GetSCIAId(speckleElement), name, curves, admMaterial, thickness)
            {
                // Color = $"#{_random.Next(0x1000000):X6}".ToColorUint(),
                Alignment = ReferenceSurfaceToNative(speckleElement.property.refSurface),
                Type = PropertyType2DToNative(speckleElement.property.type),
                // InternalNodes = internalNodes,
                // todo LCSAdjustment = new LCSAdjustment<CurveLCSType>(CurveLCSType.VectorZ)
                //{
                //    Rotation = UnitsNet.Angle.FromDegrees(speckleElement.orientationAngle),
                //    X = UnitsNet.Length.FromMeters(vectorZ[0]), // XYZ values must be specified, else SAF exporter will complain
                //    Y = UnitsNet.Length.FromMeters(vectorZ[1]),
                //    Z = UnitsNet.Length.FromMeters(vectorZ[2]),
                //},
            };

            // Assign offset property
            if (!IsAlmostZero(speckleElement.offset))
            {
                elementNative.AnalysisEccentricityZ = GetUnitsNetLength(speckleElement.offset, speckleElement.units);
            }
            // Assign rotation property
            if (!IsAlmostZero(speckleElement.orientationAngle))
            {
                var lcs = elementNative.LCSAdjustment;
                lcs.Rotation = UnitsNet.Angle.FromDegrees(speckleElement.orientationAngle);
                elementNative.LCSAdjustment = lcs;
            }

            // Assign layer
            string layer = GetSpeckleDynamicLayer(speckleElement);
            if (layer != null)
            {
                elementNative.Layer = layer;
            }
            // Assign behaviour
            if (Enum.TryParse(GetSpeckleDynamicBehaviour(speckleElement), out Member2DBehaviour behaviour))
            {
                elementNative.Behaviour = behaviour;
            }

            AddToAnalysisModel(elementNative, speckleElement);

            return elementNative;
        }


        public Member2DAlignment ReferenceSurfaceToNative(Structural.ReferenceSurface alignment)
        {
            return alignment switch
            {
                Structural.ReferenceSurface.Bottom => Member2DAlignment.Bottom,
                Structural.ReferenceSurface.Top => Member2DAlignment.Top,
                _ => Member2DAlignment.Centre,
            };
        }

        public Member2DType PropertyType2DToNative(Structural.PropertyType2D type)
        {
            return type switch
            {
                Structural.PropertyType2D.Plate => Member2DType.Plate,
                Structural.PropertyType2D.Wall => Member2DType.Wall,
                Structural.PropertyType2D.Shell => Member2DType.Shell,
                _ => throw new NotImplementedException($"No conversion support for Member2DType '{type}'"),
            };
        }

        #endregion


        #region --- TO SPECKLE ---
        public Element2D SurfaceMemberToSpeckle(StructuralSurfaceMember admElement)
        {
            if (ExistsInContext(admElement, out Element2D contextObject))
                return contextObject;
            
            // Convert 2D property
            string propName = $"{admElement.Name}_property";
            var speckleMaterial = MaterialToSpeckle(admElement.Material);

            Property2D speckleProperty = new Property2D(propName,
                speckleMaterial,
                Member2DTypeToSpeckle(admElement.Type?.Value ?? Member2DType.Other),
                admElement.Thickness.ThicknessFirst.Meters)
            {
                refSurface = Member2DAlignmentToSpeckle(admElement.Alignment)
            };

            // Convert geometry
            var curve = CurveToSpeckle(admElement.Edges, out List<Node> nodes);

            // Convert internal nodes = nodes connected to this StructuralCurveMember instance but are not part of the geometry
            // var internalNodes = admElement.InternalNodes.Select(n => NodeToSpeckle(n)).ToList();

            // Store the orientation of the beam in the Speckle object
            // Determine whether to use an OrientationNode or an Axis property for the orientation
            var admLCS = admElement.LCSAdjustment;

            // TODO: store LCS
            ///* LCS definition, see https://www.saf.guide/en/stable/getting-started/introduction.html
            // todo create element mesh / geometry
            
            var rotation = admLCS.Rotation.Degrees;
            Element2D speckleElement = new Element2D(nodes, speckleProperty)
            {
                name = admElement.Name,
                offset = admElement.AnalysisEccentricityZ.Meters,
                orientationAngle = rotation,
                // topology = internalNodes, // topology already stores the main nodes
                // todo define Parent if any (also exists in ADM)
            };

            speckleElement[SpeckleDynamicPropertyName_Layer] = admElement.Layer;
            speckleElement[SpeckleDynamicPropertyName_Behaviour] = admElement.Behaviour.ToString();

            AddToSpeckleModel(speckleElement, admElement);
            return speckleElement;
        }

        public Structural.ReferenceSurface Member2DAlignmentToSpeckle(Member2DAlignment alignment)
        {
            return alignment switch
            {
                Member2DAlignment.Bottom => Structural.ReferenceSurface.Bottom,
                Member2DAlignment.Top => Structural.ReferenceSurface.Top,
                _ => Structural.ReferenceSurface.Middle,
            };
        }

        public Structural.PropertyType2D Member2DTypeToSpeckle(Member2DType type)
        {
            return type switch
            {
                Member2DType.Plate => Structural.PropertyType2D.Plate,
                Member2DType.Wall => Structural.PropertyType2D.Wall,
                Member2DType.Shell => Structural.PropertyType2D.Shell,
                _ => throw new NotImplementedException($"No conversion support for Member2DType '{type}'"),
            };
        }
        #endregion
    }
}
