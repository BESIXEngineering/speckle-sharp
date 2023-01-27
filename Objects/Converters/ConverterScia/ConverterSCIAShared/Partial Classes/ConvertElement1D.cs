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
using Speckle.Core.Kits;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {

        #region --- TO SCIA ---

        private readonly Random _random = new Random();

        public List<IAnalysisObject> Element1DToNative(Element1D speckleElement)
        {
            if (ExistsInContext(speckleElement, out IEnumerable<IAnalysisObject> contextObjects))
                return contextObjects.ToList();

            // Get Curve nodes and convert curve
            ICurve curve = speckleElement.baseLine;

            StructuralPointConnection[] nodes;
            CurveGeometricalShape shape;

            if (curve == null)
            {
                if (speckleElement.end1Node == null || speckleElement.end2Node == null)
                {
                    throw new ArgumentNullException("endNode1|endNode2", "Invalid member geometry");
                }
                shape = CurveGeometricalShape.Line;

                nodes = new StructuralPointConnection[] {
                    PointToNative(speckleElement.end1Node.basePoint),
                    PointToNative(speckleElement.end2Node.basePoint)
                };
            }
            else if (curve is Geometry.Line line)
            {
                shape = CurveGeometricalShape.Line;

                nodes = new StructuralPointConnection[] {
                    PointToNative(line.start),
                    PointToNative(line.end)
                };
            }
            else if (curve is Geometry.Arc arc)
            {
                shape = CurveGeometricalShape.Arc3P;

                nodes = new StructuralPointConnection[] {
                    PointToNative(arc.startPoint),
                    PointToNative(arc.midPoint),
                    PointToNative(arc.endPoint)
                };
            }
            else
            {
                throw new NotImplementedException();
            }

            var results = new List<IAnalysisObject>();

            // Case rigid arm
            if (speckleElement.type == ElementType1D.Link)
            {
                if (shape != CurveGeometricalShape.Line) throw new ArgumentException("Link members must be linear");

                bool hingeAtStart = speckleElement.end1Releases != null && speckleElement.end1Releases.code == SpeckleRestraintCodeHinged;
                bool hingeAtEnd = speckleElement.end2Releases != null && speckleElement.end2Releases.code == SpeckleRestraintCodeHinged;

                HingesPosition hingePosition = hingeAtStart ? (hingeAtEnd ? HingesPosition.Both : HingesPosition.Begin) : (hingeAtEnd ? HingesPosition.End : HingesPosition.None);

                string name = speckleElement.name ?? string.Format("RA{0}", string.Join("", nodes.Select(n => n.Name)));
                RelConnectsRigidLink elementNative = new RelConnectsRigidLink(GetSCIAId(speckleElement), name, nodes)
                {
                    HingesPosition = hingePosition,
                };

                results.Add(elementNative);
            }

            // Case general beam
            else
            {
                Curve<StructuralPointConnection> curveNative = new Curve<StructuralPointConnection>(shape, nodes);
                var profile = Property1DToNative(speckleElement.property ?? throw new ArgumentNullException("Element1D.property"));
                string name = speckleElement.name ?? GetUniqueADMName<StructuralCurveMember>("B");  //string.Format("B{0}", string.Join("", nodes.Select(n => n.Name)));

                var behaviour = speckleElement.type switch
                {
                    ElementType1D.Rod => CurveBehaviour.AxialForceOnly,
                    ElementType1D.Strut => CurveBehaviour.CompressionOnly,
                    ElementType1D.Tie => CurveBehaviour.TensionOnly,
                    ElementType1D.Cable => CurveBehaviour.TensionOnly,
                    _ => CurveBehaviour.Standard,
                };
                var type = speckleElement.type switch
                {
                    ElementType1D.Beam => Member1DType.Beam,
                    ElementType1D.Column => Member1DType.Column,
                    _ => Member1DType.General,
                };

                StructuralCurveMember elementNative = new StructuralCurveMember(GetSCIAId(speckleElement), name, new[] { curveNative }, profile)
                {
                    // Color = $"#{_random.Next(0x1000000):X6}".ToColorUint(),
                    Type = type,
                    Behaviour = behaviour
                };

                // Member LCS adjustment, if needed
                if (speckleElement.localAxis != null)
                {
                    var vectorZ = speckleElement.localAxis.normal;

                    elementNative.LCSAdjustment = new LCSAdjustment<CurveLCSType>(CurveLCSType.VectorZ)
                    {
                        Rotation = UnitsNet.Angle.FromDegrees(speckleElement.orientationAngle),
                        X = UnitsNet.Length.FromMeters(vectorZ.x), // XYZ values must be specified, else SAF exporter will complain
                        Y = UnitsNet.Length.FromMeters(vectorZ.y),
                        Z = UnitsNet.Length.FromMeters(vectorZ.z),
                    };
                }
                else if (!IsAlmostZero(speckleElement.orientationAngle))
                {
                    elementNative.LCSAdjustment = new LCSAdjustment<CurveLCSType>(CurveLCSType.Standard)
                    {
                        Rotation = UnitsNet.Angle.FromDegrees(speckleElement.orientationAngle),
                        X = UnitsNet.Length.Zero,
                        Y = UnitsNet.Length.Zero,
                        Z = UnitsNet.Length.Zero
                    };
                }

                // Assign offset properties
                if (speckleElement.end1Offset != null)
                {
                    elementNative.AnalysisEccentricityYBegin = GetUnitsNetLength(speckleElement.end1Offset.y, speckleElement.end1Offset.units);
                    elementNative.AnalysisEccentricityZBegin = GetUnitsNetLength(speckleElement.end1Offset.z, speckleElement.end1Offset.units);
                }
                if (speckleElement.end2Offset != null)
                {
                    elementNative.AnalysisEccentricityYEnd = GetUnitsNetLength(speckleElement.end2Offset.y, speckleElement.end2Offset.units);
                    elementNative.AnalysisEccentricityZEnd = GetUnitsNetLength(speckleElement.end2Offset.z, speckleElement.end2Offset.units);
                }
                
                // Assign internal nodes
                if (speckleElement.topology != null && speckleElement.topology.Any())
                {
                    var geometryNodes = nodes.Select(n => n.Name);
                    // Find the internal nodes, i.e. the nodes not part of the geometry
                    // TODO: is this correct, or should we only exclude the first and last node?
                    var internalNodes = speckleElement.topology
                        .Select(n => NodeToNative(n).OfType<StructuralPointConnection>().First())
                        .Where(n => !geometryNodes.Contains(n.Name))
                        .ToList();
                    if (internalNodes.Any())
                    {
                        elementNative.InternalNodes = internalNodes;
                    }
                }

                // Assign layer
                string layer = GetSpeckleDynamicLayer(speckleElement);
                if (layer != null)
                {
                    elementNative.Layer = layer;
                }

                results.Add(elementNative);

                // If needed, add hinges to the ends of the beams.
                // From https://help.scia.net/20.0/en/rb/modelling/elements_of_a_model.htm?TocPath=Modelling%7CGeometry%7C_____1:
                //  "If two 1D members have a common end point, the connection of the 1D members in this point (or node) is normally rigid"
                
                var r1 = speckleElement.end1Releases;
                var r2 = speckleElement.end2Releases;
                bool identicalRestraints = r1 != null && r1.code != null && r2 != null 
                    && r1.code.Equals(r2.code, StringComparison.InvariantCultureIgnoreCase);

                if (r1 != null && r1.code != SpeckleRestraintCodeFixed)
                {
                    name = GetSpeckleDynamicName(r1) ?? GetUniqueADMName<RelConnectsStructuralMember>("H");
                    var hingeOnBeam = new RelConnectsStructuralMember(GetSCIAId(r1), name, elementNative)
                    {
                        Position = identicalRestraints ? Position.Both: Position.Begin
                    };
                    SetADMRestraint(hingeOnBeam, r1.code);

                    results.Add(hingeOnBeam);
                }
                if (r2 != null && r2.code != SpeckleRestraintCodeFixed && !identicalRestraints)
                {
                    name = GetSpeckleDynamicName(r2) ?? GetUniqueADMName<RelConnectsStructuralMember>("H");
                    var hingeOnBeam = new RelConnectsStructuralMember(GetSCIAId(r2), name, elementNative)
                    {
                        Position = Position.End
                    };
                    SetADMRestraint(hingeOnBeam, r2.code);

                    results.Add(hingeOnBeam);
                }
            }
            // TODO: Element1D set advanced specs (LCSAdjustment, StructuralEccentricity, ...)

            AddToAnalysisModel(results, speckleElement);

            return results;
        }
        #endregion


        #region --- TO SPECKLE ---
        public Element1D CurveMemberToSpeckle(StructuralCurveMember admElement)
        {
            if (ExistsInContext(admElement, out Element1D contextObject))
                return contextObject;

            var speckleProperty = CrossSectionToSpeckle(admElement.CrossSection);

            // Convert geometry
            var curve = CurveToSpeckle(admElement.Curves, out List<Node> nodes);
            if (!(curve is Geometry.Line line))
            {
                throw new NotImplementedException("Can currently only convert linear ADM StructuralCurveMembers");
            }

            // Convert internal nodes = nodes connected to this StructuralCurveMember instance but are not part of the geometry
            // TODO: Currently store them under topology, but this is probably not 100% correct as the topology should hold ALL nodes of the element
            var internalNodes = admElement.InternalNodes.Select(n => NodeToSpeckle(n)).ToList();

            // Offset/eccentricity (AnalysisEccentricity DOES affect the forces in SCIA, StructuralEccentricity DOES NOT)
            var eccBegin = new Geometry.Vector(0, admElement.AnalysisEccentricityYBegin.Meters, admElement.AnalysisEccentricityZBegin.Meters, Units.Meters);
            var eccEnd = new Geometry.Vector(0, admElement.AnalysisEccentricityYEnd.Meters, admElement.AnalysisEccentricityZEnd.Meters, Units.Meters);

            // Store the orientation of the beam in the Speckle object
            // Determine whether to use an OrientationNode or an Axis property for the orientation
            var admLCS = admElement.LCSAdjustment;
            
            /* LCS definition, see https://www.saf.guide/en/stable/getting-started/introduction.html
             Always X-axis orientation is given by system line of the member
                X-axis direction is defined by the start point and end point of the member
                
             When is LCS enum set on "by point":
                Z-axis orientation is given by the intersection of a plane perpendicular to x and a plane defined by x-axis and point
                Z-axis direction follow the point
                Y-axis orientation and direction is defined by the right-hand rule
            
             When is LCS enum set on "by vector":
                Z-axis orientation and direction is given by vector coordinates
                Y-axis orientation and direction is defined by right-hand rule*/

            Geometry.Vector xDir, yDir, zDir;
            Geometry.Point origin = line.start;
            xDir = curve.GetDirection();

            // ToDo: check whether CurveLCSType.Standard orients by Y, by Z or by some other setting
            switch (admLCS.LCS)
            {
                case CurveLCSType.VectorY:
                case CurveLCSType.Standard:
                    yDir = new Geometry.Vector(admLCS.X.Value.Meters, admLCS.Y.Value.Meters, admLCS.Z.Value.Meters);
                    zDir = Geometry.Vector.CrossProduct(xDir, yDir);
                    break;
                case CurveLCSType.VectorZ:
                    zDir = new Geometry.Vector(admLCS.X.Value.Meters, admLCS.Y.Value.Meters, admLCS.Z.Value.Meters);
                    yDir = Geometry.Vector.CrossProduct(zDir, xDir);
                    break;
                case CurveLCSType.PointY:
                    var vecToPoint = new Geometry.Vector(admLCS.X.Value.Meters - origin.x, admLCS.Y.Value.Meters - origin.y, admLCS.Z.Value.Meters - origin.z);
                    zDir = Geometry.Vector.CrossProduct(xDir, vecToPoint).Unit();
                    yDir = Geometry.Vector.CrossProduct(zDir, xDir);
                    break;
                case CurveLCSType.PointZ:
                    vecToPoint = new Geometry.Vector(admLCS.X.Value.Meters - origin.x, admLCS.Y.Value.Meters - origin.y, admLCS.Z.Value.Meters - origin.z);
                    yDir = Geometry.Vector.CrossProduct(vecToPoint, xDir).Unit();
                    zDir = Geometry.Vector.CrossProduct(xDir, yDir);
                    break;
                case CurveLCSType.FromUCS:
                default:
                    throw new NotImplementedException($"No support for CurveLCSType {admLCS.LCS}");
            }

            var speckleOrientation = new Geometry.Plane(origin, zDir, xDir, yDir);
            var rotation = admLCS.Rotation.Degrees;

            Element1D speckleElement = new Element1D()
            {
                name = admElement.Name,
                property = speckleProperty,
                baseLine = line,
                type = Member1DTypeToSpeckle(admElement.Type.Value, admElement.Behaviour),
                localAxis = speckleOrientation,
                end1Offset = eccBegin,
                end2Offset = eccEnd,
                orientationAngle = rotation,
                end1Node = nodes[0],
                end2Node = nodes.Last(),
                topology = internalNodes,
                // todo define Parent if any (also exists in ADM)
            };

            speckleElement[SpeckleDynamicPropertyName_Layer] = admElement.Layer;

            AddToSpeckleModel(speckleElement, admElement);
            return speckleElement;
        }

        public Element1D RigidLinkToSpeckle(RelConnectsRigidLink admLink)
        {
            if (ExistsInContext(admLink, out Element1D contextObject))
                return contextObject;

            var nodes = admLink.Nodes.Select(n => NodeToSpeckle(n)).ToList();


            Restraint restraintAtStart = (admLink.HingesPosition == HingesPosition.Begin || admLink.HingesPosition == HingesPosition.Both) 
                ? new Restraint(RestraintType.Pinned) : null;
            Restraint restraintAtEnd = (admLink.HingesPosition == HingesPosition.End || admLink.HingesPosition == HingesPosition.Both) 
                ? new Restraint(RestraintType.Pinned) : null;

            Element1D speckleElement = new Element1D()
            {
                name = admLink.Name,
                property = null,
                end1Node = nodes[0],
                end2Node = nodes[1],
                end1Releases = restraintAtStart,
                end2Releases = restraintAtEnd,
                baseLine = new Geometry.Line(nodes[0].basePoint, nodes[1].basePoint),
                type = ElementType1D.Link,
            };

            AddToSpeckleModel(speckleElement, admLink);
            return speckleElement;
        }

        public Restraint HingeOnBeamToSpeckle(RelConnectsStructuralMember admHingeOnBeam)
        {
            Element1D speckleMember = CurveMemberToSpeckle(admHingeOnBeam.Member);
            Restraint speckleRestraint = ConstraintToSpeckle(admHingeOnBeam);

            if (admHingeOnBeam.Position == Position.Begin || admHingeOnBeam.Position == Position.Both)
            {
                speckleMember.end1Releases = speckleRestraint;
            }
            if (admHingeOnBeam.Position == Position.End || admHingeOnBeam.Position == Position.Both)
            {
                speckleMember.end2Releases = speckleRestraint;
            }
            return speckleRestraint;
        }

        public ICurve CurveToSpeckle(IReadOnlyList<Curve<StructuralPointConnection>> curves, out List<Node> nodes)
        {
            if (curves == null || !curves.Any())
            {
                nodes = null;
                return null; 
            }

            if (curves.Count == 1)
            {
                return CurveToSpeckle(curves.First(), out nodes);
            }

            int count = curves.Count;
            nodes = new List<Node>(count + 1);
            List<ICurve> segments = new List<ICurve>(count);
            List<Node> curveNodes;

            for (int i = 0; i<count; i++)
            {
                segments.Add(CurveToSpeckle(curves[i], out curveNodes));
                if (i == 0)
                {
                    nodes.AddRange(curveNodes);
                }
                else
                {
                    nodes.AddRange(curveNodes.Skip(1));
                }
            }

            // Check for closed curve by node name (todo: better to check by coordinate value?)
            bool closed = count > 1 && nodes.First().name == nodes.Last().name;
            if (closed)
            {
                nodes.RemoveAt(count);
            }

            return new Geometry.Polycurve() { segments = segments, closed = closed };
        }

        public ICurve CurveToSpeckle(Curve<StructuralPointConnection> admCurve, out List<Node> nodes)
        {
            nodes = admCurve.Nodes.Select(n => NodeToSpeckle(n)).ToList();
            var points = nodes.Select(n => n.basePoint).ToList();
            
            switch (admCurve.GeometricalShape)
            {
                case CurveGeometricalShape.Line:
                    return new Geometry.Line(points[0], points[1]);

                case CurveGeometricalShape.Arc3P:
                    return new Geometry.Arc() { startPoint = points[0], midPoint = points[1], endPoint = points[2] };

                default:
                    throw new NotImplementedException($"No conversion for ADM Curve of type {admCurve.GeometricalShape}");
            }
        }

        public ElementType1D Member1DTypeToSpeckle(Member1DType type, CurveBehaviour behaviour)
        {
            switch (behaviour)
            {
                case CurveBehaviour.AxialForceOnly:
                    return ElementType1D.Rod;
                case CurveBehaviour.CompressionOnly:
                    return ElementType1D.Strut;
                case CurveBehaviour.TensionOnly:
                    return ElementType1D.Tie;
                default:
                    return type switch
                    {
                        Member1DType.Beam => ElementType1D.Beam,
                        Member1DType.Column => ElementType1D.Column,
                        _ => ElementType1D.Other,
                    };
            }
        }
        #endregion
    }
}
