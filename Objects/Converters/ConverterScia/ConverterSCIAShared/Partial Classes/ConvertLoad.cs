using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Loads;
using ModelExchanger.AnalysisDataModel.Subtypes;
using ModelExchanger.AnalysisDataModel.StructuralElements;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Points;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Curves;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects.Structural.Geometry;
using Objects.Structural.Loading;
using Objects.Structural.SCIA.Loading;
using Speckle.Core.Models;
using Speckle.Core.Kits;
using ModelExchanger.AnalysisDataModel.Interfaces;

namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---
        public List<IAnalysisObject> NodeLoadToNative(LoadNode speckleLoad)
        {
            if (ExistsInContext(speckleLoad, out IEnumerable<IAnalysisObject> contextObjects))
                return contextObjects.ToList();

            if (speckleLoad.nodes == null || !speckleLoad.nodes.Any()) throw new ArgumentNullException("LoadNode.nodes", "List cannot be null or empty");

            var admLoadCase = LoadCaseToNative(speckleLoad.loadCase);
            ActionDirection direction = LoadDirectionToNative(speckleLoad.direction);

            // TODO add LoadAxis support to set the correct coordinate system

            int nodeCount = speckleLoad.nodes.Count;
            List<IAnalysisObject> admLoads = new List<IAnalysisObject>(nodeCount);

            foreach (var node in speckleLoad.nodes)
            {
                var admNode = NodeToNative(node).OfType<StructuralPointConnection>().First();
                Guid id = nodeCount > 1 ? GetSCIAId() : GetSCIAId(speckleLoad);

                string loadName = speckleLoad.name;
                if (string.IsNullOrWhiteSpace(loadName))
                    loadName = GetUniqueADMName<StructuralPointAction<PointStructuralReferenceOnPoint>>("F");
                else if (nodeCount > 1)
                    loadName = GetUniqueADMName<StructuralCurveAction<CurveStructuralReferenceOnBeam>>(loadName);

                var admLoad = new StructuralPointAction<PointStructuralReferenceOnPoint>(
                    id, loadName, UnitsNet.Force.FromKilonewtons(speckleLoad.value),
                    admLoadCase, PointForceAction.InNode, new PointStructuralReferenceOnPoint(admNode))
                {
                    Direction = direction,
                    CoordinateSystem = CoordinateSystem.Global,
                };

                admLoads.Add(admLoad);
            }

            AddToAnalysisModel(admLoads, speckleLoad);
            return admLoads;
        }
        

        public List<IAnalysisObject> BeamLoadToNative(LoadBeam speckleLoad)
        {
            if (ExistsInContext(speckleLoad, out IEnumerable<IAnalysisObject> contextObjects))
                return contextObjects.ToList();

            if (speckleLoad.values == null || !speckleLoad.values.Any()) throw new ArgumentNullException("LoadBeam.values", "List cannot be null or  empty");
            if (speckleLoad.elements == null || !speckleLoad.elements.Any()) throw new ArgumentNullException("LoadBeam.elements", "List cannot be null or empty");

            // TODO: set unit (currently unit property is "m", so not yet very useful...)
            // var forceValues = speckleLoad.values.Select(v => UnitsNet.ForcePerLength.FromKilonewtonsPerMeter(v)).ToArray();
            var admLoadCase = LoadCaseToNative(speckleLoad.loadCase);

            // TODO: check to set correct loading directions based on local / global axes
            ActionDirection direction = LoadDirectionToNative(speckleLoad.direction);
            CoordinateSystem cs = LoadAxisTypeToNative(speckleLoad.loadAxisType);

            Location location = speckleLoad.isProjected ? Location.Projection : Location.Length;

            bool isPointLoad = false;
            Origin origin = Origin.FromStart;
            CurveDistribution distribution = CurveDistribution.Uniform;
            ExtentOfForceOnBeam extent = ExtentOfForceOnBeam.Full;

            UnitsNet.Length? startAbsolute = null;
            UnitsNet.Length? endAbsolute = null;
            double forceAtStart = speckleLoad.values[0];
            double? forceAtEnd = null;
            
            switch (speckleLoad.loadType)
            {
                case BeamLoadType.Point:
                    isPointLoad = true;
                    startAbsolute = GetUnitsNetLength(speckleLoad.positions[0], speckleLoad.units);
                    // Todo: implement repetition in case of multiple values at positions with constant interval?
                    break; 

                case BeamLoadType.Uniform:
                    forceAtEnd = forceAtStart;
                    break;

                case BeamLoadType.Linear:
                    distribution = CurveDistribution.Trapezoidal;
                    forceAtEnd = speckleLoad.values.Last();
                    break;

                case BeamLoadType.Patch:
                    if (speckleLoad.positions.Count < 2) throw new ArgumentException("Insufficient load positions defined");
                    distribution = CurveDistribution.Trapezoidal;
                    extent = ExtentOfForceOnBeam.Span;
                    startAbsolute = GetUnitsNetLength(speckleLoad.positions[0], speckleLoad.units);
                    endAbsolute = GetUnitsNetLength(speckleLoad.positions[1], speckleLoad.units);
                    forceAtEnd = speckleLoad.values.Last();
                    break;

                default:
                    throw new NotImplementedException();
            }

            int elementCount = speckleLoad.elements.Count;
            List<IAnalysisObject> admLoads = new List<IAnalysisObject>(elementCount);
            
            foreach (var _item in speckleLoad.elements)
            {
                if (_item is Element1D element)
                {
                    var admElement = Element1DToNative(element).OfType<StructuralCurveMember>().First();

                    string loadName = speckleLoad.name;
                    if (string.IsNullOrWhiteSpace(loadName))
                        loadName = GetUniqueADMName<StructuralCurveAction<CurveStructuralReferenceOnBeam>>(isPointLoad ? "Fb" : "LF");
                    else if (elementCount > 1)
                        loadName = GetUniqueADMName<StructuralCurveAction<CurveStructuralReferenceOnBeam>>(loadName);

                    Guid id = elementCount > 1 ? GetSCIAId() : GetSCIAId(speckleLoad);

                    if (isPointLoad)
                    {
                        var admPointReference = new PointStructuralReferenceOnBeam(admElement)
                        {
                            Origin = origin,
                            CoordinateDefinition = CoordinateDefinition.Absolute,
                            AbsolutePositionX = startAbsolute,
                        };

                        var forceAtPoint = UnitsNet.Force.FromKilonewtons(forceAtStart);
                        var admLoad = new StructuralPointAction<PointStructuralReferenceOnBeam>(
                            id, loadName, forceAtPoint, admLoadCase, PointForceAction.OnBeam, admPointReference)
                        {
                            CoordinateSystem = cs,
                            Direction = direction,
                        };
                        admLoads.Add(admLoad);
                    }
                    else
                    {
                        var forceOnBeam1 = UnitsNet.ForcePerLength.FromKilonewtonsPerMeter(forceAtStart);

                        // TODO add support for ForceAction OnEdge or OnRib
                        var admLoad = new StructuralCurveAction<CurveStructuralReferenceOnBeam>(
                            id, loadName, CurveForceAction.OnBeam, forceOnBeam1, admLoadCase, new CurveStructuralReferenceOnBeam(admElement))
                        {
                            CoordinateSystem = cs,
                            Direction = direction,
                            Distribution = distribution,
                            Location = location,
                            Origin = origin,
                            CoordinateDefinition = CoordinateDefinition.Relative,
                            Extent = extent,
                        };
                        if (extent == ExtentOfForceOnBeam.Span)
                        {
                            admLoad.CoordinateDefinition = CoordinateDefinition.Absolute;
                            admLoad.StartPointAbsolute = startAbsolute;
                            admLoad.EndPointAbsolute = endAbsolute;
                        }
                        if (forceAtEnd != null)
                        {
                            admLoad.Value2 = UnitsNet.ForcePerLength.FromKilonewtonsPerMeter((double)forceAtEnd);
                        }
                        admLoads.Add(admLoad);
                    }
                }
                else
                {
                    throw new NotImplementedException($"Can't add BeamLoads to elements of type {_item.GetType()}");
                }
            }

            AddToAnalysisModel(admLoads, speckleLoad);
            return admLoads;
        }

        public ActionDirection LoadDirectionToNative(LoadDirection direction)
        {
            // TODO: add support for bending moment loads case LoadDirection.XX YY or ZZ
            return direction switch
            {
                LoadDirection.X => ActionDirection.X,
                LoadDirection.Y => ActionDirection.Y,
                LoadDirection.Z => ActionDirection.Z,
                _ => throw new NotImplementedException("Moments not yet implemented as load"),
            };
        }

        public CoordinateSystem LoadAxisTypeToNative(Structural.LoadAxisType axisType)
        {
            return axisType switch
            {
                Structural.LoadAxisType.Global => CoordinateSystem.Global,
                Structural.LoadAxisType.Local => CoordinateSystem.Local,
                _ => throw new NotImplementedException(),
            };
        }

        #endregion


        #region --- TO SPECKLE ---
        public LoadNode NodeLoadToSpeckle(StructuralPointAction<PointStructuralReferenceOnPoint> admPointLoad)
        {
            if (ExistsInContext(admPointLoad, out LoadNode contextObject))
                return contextObject;

            var speckleLoadCase = LoadCaseToSpeckle(admPointLoad.LoadCase);

            LoadNode speckleLoad = new LoadNode()
            {
                name = admPointLoad.Name,
                direction = ActionDirectionToSpeckle(admPointLoad.Direction),
                value = admPointLoad.Value.Kilonewtons,
                units = "kN",
                loadCase = speckleLoadCase,
                nodes = new List<Node> { NodeToSpeckle(admPointLoad.StructuralReference.ReferenceNode) },
            };

            AddToSpeckleModel(speckleLoad, admPointLoad);
            return speckleLoad;
        }

        public LoadBeam BeamLoadToSpeckle(StructuralPointAction<PointStructuralReferenceOnBeam> admPointLoad)
        {
            if (ExistsInContext(admPointLoad, out LoadBeam contextObject))
                return contextObject;

            var admReference = admPointLoad.StructuralReference;
            var speckleLoadCase = LoadCaseToSpeckle(admPointLoad.LoadCase);
            var speckleElement = CurveMemberToSpeckle(admReference.ReferenceMember);

            // Determine the point load absolute positions along the beam
            List<double> positions = new List<double>(admReference.Repeat);
            List<double> values = new List<double>(admReference.Repeat);
            double length = admReference.ReferenceMember.Length.Meters;

            double xStart, xDelta;
            if (admReference.CoordinateDefinition == CoordinateDefinition.Absolute)
            {
                xStart = admReference.AbsolutePositionX?.Meters ?? throw new ArgumentException("No point load position available");
                xDelta = admReference.AbsoluteDeltaX?.Meters ?? 0;
            }
            else
            {
                xStart = (admReference.RelativePositionX ?? throw new ArgumentException("No point load position available")) * length;
                xDelta = admReference.RelativeDeltaX != null ? (double)admReference.RelativeDeltaX * length : 0;
            }

            double force = admPointLoad.Value.Kilonewtons;
            for (int i = 0; i < admReference.Repeat; i++)
            {
                double p = xStart + xDelta * i;
                if (admReference.Origin == Origin.FromEnd)
                    p = length - p;
                positions.Add(p);
                values.Add(force);
            }

            LoadBeam speckleLoad = new LoadBeam()
            {
                name = admPointLoad.Name,
                loadType = BeamLoadType.Point,
                direction = ActionDirectionToSpeckle(admPointLoad.Direction),
                values = values,
                positions = positions,
                units = null,
                loadCase = speckleLoadCase,
                elements = new List<Base> { speckleElement },
            };

            AddToSpeckleModel(speckleLoad, admPointLoad);
            return speckleLoad;
        }

        public LoadBeam BeamLoadToSpeckle(StructuralCurveAction<CurveStructuralReferenceOnBeam> admBeamLoad)
        {
            if (ExistsInContext(admBeamLoad, out LoadBeam contextObject))
                return contextObject;

            var admReference = admBeamLoad.StructuralReference;
            var speckleLoadCase = LoadCaseToSpeckle(admBeamLoad.LoadCase);
            var speckleElement = CurveMemberToSpeckle(admReference.ReferenceMember);


            // Determine the point load absolute positions along the beam
            List<double> positions;
            List<double> values;

            double length = admReference.ReferenceMember.Length.Meters;
            double positionStart, positionEnd;
            if (admBeamLoad.CoordinateDefinition == CoordinateDefinition.Absolute)
            {
                positionStart = admBeamLoad.StartPointAbsolute?.Meters ?? 0;
                positionEnd = admBeamLoad.EndPointAbsolute?.Meters ?? length;
            }
            else
            {
                positionStart = (admBeamLoad.StartPointRelative ?? 0) * length;
                positionEnd = (admBeamLoad.EndPointRelative ?? 1) * length;
            }

            double valueStart, valueEnd;
            valueStart = admBeamLoad.Value1.KilonewtonsPerMeter;
            valueEnd = admBeamLoad.Value2?.KilonewtonsPerMeter ?? valueStart;

            BeamLoadType loadType;
            if (admBeamLoad.Extent == ExtentOfForceOnBeam.Span)
            {
                loadType = BeamLoadType.Patch; // two values and positions
                positions = new List<double> { positionStart, positionEnd };
                values = new List<double> { valueStart, valueEnd };
            }
            else if (admBeamLoad.Value2 != null)
            {
                loadType = BeamLoadType.Linear; // two values and positions
                positions = new List<double> { positionStart, positionEnd };
                values = new List<double> { valueStart, valueEnd };
            }
            else
            {
                loadType = BeamLoadType.Uniform; // one value and position
                positions = new List<double> { positionStart };
                values = new List<double> { valueStart };
            }


            LoadBeam speckleLoad = new LoadBeam()
            {
                name = admBeamLoad.Name,
                loadType = loadType,
                direction = ActionDirectionToSpeckle(admBeamLoad.Direction),
                values = values,
                positions = positions,
                units = "kN/m",
                loadCase = speckleLoadCase,
                elements = new List<Base> { speckleElement },
            };

            AddToSpeckleModel(speckleLoad, admBeamLoad);
            return speckleLoad;
        }

        public LoadDirection ActionDirectionToSpeckle(ActionDirection direction)
        {
            // TODO: add support for bending moment loads case LoadDirection.XX YY or ZZ
            return direction switch
            {
                ActionDirection.X => LoadDirection.X,
                ActionDirection.Y => LoadDirection.Y,
                ActionDirection.Z => LoadDirection.Z,
                _ => throw new NotImplementedException("Moments not yet implemented as load"),
            };
        }
        #endregion
    }
}
