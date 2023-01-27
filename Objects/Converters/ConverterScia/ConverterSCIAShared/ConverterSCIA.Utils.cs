using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Contracts;
using ModelExchanger.AnalysisDataModel.Integration.Bootstrapper;
using ModelExchanger.AnalysisDataModel.StructuralElements;
using CSInfrastructure.IOC;
using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;

using Speckle.Core.Models;
using Speckle.Core.Kits;
using Objects.Geometry;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ModelExchanger.AnalysisDataModel.Models;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region -- SCIA OBJECT TRACING --
        const string ADM_APP_ID_PREFIX = "ADM.";

        /// <summary>
        /// Checks whether a Speckle Base object with the same Base.applicationId has been converted already to SCIA.
        /// Note: a single Base.applicationId can refer to multiple SCIA ids.
        /// </summary>
        public bool ExistsInContext<T>(Base @base, out IEnumerable<T> objects)
        {
            string applicationId = @base.applicationId;
            if (applicationId != null)
            {
                // The ContextObjects hold all Speckle Base objects that have been converted
                objects = ContextObjects
                        .Where(placeholder => placeholder.OriginalId == applicationId && placeholder.Converted.OfType<T>().Any())
                        .SelectMany(placeholder => placeholder.Converted.OfType<T>());
/*#if DEBUG
                if (objects.Any()) { Report.Log($"Retrieved {@base.GetType().Name} {@base.applicationId} from context"); }
#endif*/
                return objects.Any();
            }
            else
            {
                objects = Enumerable.Empty<T>();
                return false;
            }
        }

        /// <summary>
        /// Checks whether a SCIA ADM object with the same Id has been converted already to Speckle.
        /// </summary>
        public bool ExistsInContext<T>(IAnalysisObject admObject, out T result) where T : Base
        {
            result = null;
            if (SpeckleContext == null || admObject == null) return false;

            var key = admObject.Id;
            if (SpeckleContext.ContainsKey(key))
            {
                result = SpeckleContext[key] as T ?? throw new Exception("Object with same ADM Id does not give same Speckle type!");
                return true;
            }
            return false;
        }

        private T GetAdmObjectByName<T>(string name) where T: IAnalysisObject
        {
            return AdmModel.OfType<T>().FirstOrDefault(obj => name.Equals(obj.Name, StringComparison.InvariantCultureIgnoreCase));
        }

        /*
        /// <summary>
        /// Returns, if found, the corresponding doc element.
        /// The doc object can be null if the user deleted it. 
        /// </summary>
        /// <param name="applicationId">Id of the application that originally created the element, in SCIA it's the ApiGuid</param>
        /// <returns>The element, if found, otherwise null</returns>
        public IAnalysisObject GetExistingElementByApplicationId(string applicationId)
        {
            if (applicationId == null)
                return null;

            // From REVIT converter, takes the UniqueId and checks if it was created in the previous receiving of the stream, 
            // and if not, whether the element nevertheless exists in the Revit document.
            var @ref = PreviousContextObjects.FirstOrDefault(o => o.applicationId == applicationId);

            if (@ref == null)
            {
                //element was not cached in a PreviousContex but might exist in the model
                //eg: user sends some objects, moves them, receives them 
                return Doc.GetElement(applicationId);
            }

            //return the cached object, if it's still in the model
            return Doc.GetElement(@ref.ApplicationGeneratedId);
            
        }
        */

        /// <summary>
        /// Add a Speckle Structural object to SpeckleModel and store the source ADM id inside the Speckle object.
        /// </summary>
        /// <param name="speckleObject"></param>
        /// <param name="admObject"></param>
        private void AddToSpeckleModel(Base speckleObject, IAnalysisObject admObject)
        {
            if (speckleObject == null) throw new ArgumentNullException(nameof(speckleObject));

            speckleObject.applicationId = $"{ADM_APP_ID_PREFIX}{admObject.Id:N}";

            if (SpeckleContext == null)
            { 
                SpeckleContext = new Dictionary<Guid, Base>();
            }
            SpeckleContext.Add(admObject.Id, speckleObject);
            Report.Log($"Created {GetObjectString(speckleObject)}");
        }

        /// <summary>
        /// Add an ADM AnalysisObject that has been converted from Speckle to Native to the AnalysisModel
        /// </summary>
        /// <param name="admObject">ADM AnalysisObject</param>
        private void AddToAnalysisModel(IAnalysisObject admObject, Base speckleObject = null)
        {
            if (admObject == null) throw new ArgumentNullException(nameof(admObject));


            /* // ToDo: check whether more efficient to use the IAnalysisModelService.AddItemsToModel method instead of adding one by one?
            if (!AdmModel.Any())
            {
                var result = modelService.AddItemsToModel(AdmModel, admQueue, null);
                {
                    Report.Log($"Created {GetObjectString(admObject)}");
                }
            }
            */
            /*
            if (!modelService.AddOrUpdate(Doc, analysisObject, null))
            { 
                Report.LogConversionError(new "Failed to add/update object in AnalysisModel: ")
            };
            */
            if (!AdmModel.Any(obj => admObject.Id == obj.Id))
            {
                if (admModelService.AddItemToModel(AdmModel, admObject, null))
                {
                    Report.Log($"Created {GetObjectString(admObject)}");
                }
                else
                {
                    Report.LogConversionError(new Exception($"Failed to add {GetObjectString(admObject)} to ADM AnalysisModel object"));  // to do, add validation error details
                }
            }
            else
            {
                if (admModelService.Update(AdmModel, admObject, null))
                {
                    Report.Log($"Updated {admObject}");
                }
                else
                {
                    Report.LogConversionError(new Exception($"Failed to update {GetObjectString(admObject)} in ADM AnalysisModel object"));  // to do, add validation error details
                }
            }

            // Add a context placeholder to indicate that the object has been converted in the current run
            // TODO is this useful? Currently not used
            AddContextPlaceholder(speckleObject, admObject);
        }

        /// <summary>
        /// Flag newly created objects to be pushed to the SCIA app.
        /// </summary>
        /// <param name="sciaObject"></param>
        private void AddToAnalysisModel(List<IAnalysisObject> sciaObjects, Base speckleObject = null)
        {
            foreach (var obj in sciaObjects)
            {
                AddToAnalysisModel(obj, speckleObject);
            }
        }

        private void ValidateModel()
        {
            if (!admModelService.ValidateModel(AdmModel))
            {
                Report.LogConversionError(new Exception("Failed to validate model"));
            }
        }

        /// <summary>
        /// Custom string representation of the IAnalysisObject as the default one only show the full Type name.
        /// </summary>
        private string GetObjectString(IAnalysisObject obj) => $"{obj.GetType().Name} {obj.Name} ({obj.Id})";

        /// <summary>
        /// Custom string representation of the Speckle Base as the default one only show the full Type name.
        /// </summary>
        private string GetObjectString(Base obj) => $"{obj.GetType().Name} {GetSpeckleDynamicName(obj) ?? ""} ({obj.applicationId})";


        /// <summary>
        /// Add Context object for a Speckle object that has been converted to Native
        /// </summary>
        /// <param name="speckleBase"></param>
        /// <param name="sciaObject"></param>
        private void AddContextPlaceholder(Base speckleBase, IAnalysisObject sciaObject)
        {
            var placeholder = new ApplicationObject(
                speckleBase?.applicationId, // speckleBase can be null, e.g. for Speckle Geometry
                speckleBase?.speckle_type);
            
            placeholder.Update(
                createdId: sciaObject.Id.ToString(),
                status: ApplicationObject.State.Created, // todo
                convertedItem: sciaObject);

            ContextObjects.Add(placeholder);
        }

        private Guid GetSCIAId() => Guid.NewGuid();

        private Guid GetSCIAId(Base obj)
        {
            // If the Speckle Base originates from SCIA, its original SCIA Id will be stored in Base.applicationId (with a fixed prefix)
            if (obj.applicationId != null 
                && obj.applicationId.StartsWith(ADM_APP_ID_PREFIX) 
                && Guid.TryParse(obj.applicationId.Substring(ADM_APP_ID_PREFIX.Length), out Guid result))
            {
                return result;
            }
            return GetSCIAId();
        }

        private Guid GetSCIAId(IAnalysisObject sciaObject)
        {
            return sciaObject.Id;
        }

        #endregion


        #region -- ENUMS -- 
        /// <summary>
        /// Get the Enum of a different type with the same string representation.
        /// </summary>
        /// <typeparam name="T">Type of the target enum</typeparam>
        /// <param name="other">Enum to convert</param>
        /// <returns></returns>
        public T GetSimilarEnum<T>(Enum other) where T : Enum
        {
            return GetSimilarEnum<T>(other.ToString());
        }
        public T GetSimilarEnum<T>(string other) where T : Enum
        {
            return (T)Enum.Parse(typeof(T), other, true);
            // Optionally add string match algorithm in case the match isn't exact https://stackoverflow.com/questions/13793560/find-closest-match-to-input-string-in-a-list-of-strings/13793600
        }
        #endregion


        #region -- NAMING --

        const string SpeckleDynamicPropertyName_Name = "name";
        const string SpeckleDynamicPropertyName_Layer = "layer";
        const string SpeckleDynamicPropertyName_Behaviour = "behaviour";
        readonly Dictionary<string, int> ADMNameIdxCounters = new Dictionary<string, int>();

        /*private string GetUniqueADMName(string prefix)
        {
            if (!ADMNameIdxCounters.TryGetValue(prefix, out int idx))
                idx = 0;

            idx += 1;
            ADMNameIdxCounters[prefix] = idx;

            return $"{prefix}{idx}";
        }*/
        /// <summary>
        /// Create a unique name for an ADM object of a given type, starting with a given prefix and followed by an auto-incrementing index.
        /// </summary>
        private string GetUniqueADMName<T>(string prefix) where T:IAnalysisObject
        {
            if (!ADMNameIdxCounters.TryGetValue(prefix, out int idx))
                idx = 0;

            idx += 1;
            string name = $"{prefix}{idx}";

            var modelNames = AdmModel.OfType<T>().Select(n => n.Name.ToLowerInvariant()).ToList();
            while (modelNames.Contains(name.ToLowerInvariant()))
            {
                idx += 1;
                name = $"{prefix}{idx}";
            }

            ADMNameIdxCounters[prefix] = idx;

            return name;
        }
        public string GetSpeckleDynamicName(Base @base)
        {
            return GetSpeckleDynamicStringProperty(@base, SpeckleDynamicPropertyName_Name);
        }
        public string GetSpeckleDynamicLayer(Base @base)
        {
            return GetSpeckleDynamicStringProperty(@base, SpeckleDynamicPropertyName_Layer);
        }
        public string GetSpeckleDynamicBehaviour(Base @base)
        {
            return GetSpeckleDynamicStringProperty(@base, SpeckleDynamicPropertyName_Behaviour);
        }

        public string GetSpeckleDynamicStringProperty(Base @base, string propertyName)
        {
            string value = null;
            foreach (string p in @base.GetDynamicMemberNames())
            {
                if (propertyName.Equals(p, StringComparison.OrdinalIgnoreCase))
                {
                    value = @base[p] as string;
                    break;
                }
            }
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion


        #region -- NODE OBJECTS --
        /// <summary>
        /// Use the current coincidence tolerence to add all nodes within a given distance to
        /// ADM StructuralCurveMembers but that are not part of their Geometry as internal nodes.
        /// </summary>
        /// <param name="model"></param>
        private void AddMemberCoincidingNodes()
        {
            var nodes = AdmModel.OfType<StructuralPointConnection>().ToList();
            foreach (var member in AdmModel.OfType<StructuralCurveMember>())
            {
                AddMemberCoincidingNodes(member, nodes);
            }
        }
        private void AddMemberCoincidingNodes(StructuralCurveMember member, IEnumerable<StructuralPointConnection> nodes)
        {
            foreach (var crv in member.Curves)
            {
                IEnumerable<StructuralPointConnection> nodesOnMember;
                var geometryNodes = crv.Nodes;

                switch (crv.GeometricalShape)
                {
                    case ModelExchanger.AnalysisDataModel.Enums.CurveGeometricalShape.Line:
                        // Todo: avoid conversion to Speckle geometry in order to improve performance
                        nodesOnMember = nodes
                            .Where(n => !geometryNodes.Contains(n) && GeometryExtensions.DistancePointToSegment(PointToSpeckle(n), PointToSpeckle(crv.NodeStart), PointToSpeckle(crv.NodeEnd)) <= CoincidenceTolerance);
                        break;
                    default:
                        throw new NotImplementedException($"Shape {crv.GeometricalShape} not supported");
                }

                if (nodesOnMember.Any())
                {
                    member.InternalNodes = nodesOnMember.Concat(member.InternalNodes).Distinct().ToList();
                    if (!admModelService.Update(AdmModel, member))
                        Report.LogConversionError(new Exception($"Failed to update internal nodes for member {member.Name}"));
                }
            }
        }

        private bool NodesCoincide(StructuralPointConnection sciaNode, UnitsNet.Length[] coordinates)
        {
            return LengthEquals(sciaNode.X, coordinates[0])
                && LengthEquals(sciaNode.Y, coordinates[1])
                && LengthEquals(sciaNode.Z, coordinates[2]);
        }
        private bool NodesCoincide(StructuralPointConnection node, Geometry.Point point)
        {
            return NodesCoincide(node, CoordinatesToNative(point));
        }

        private UnitsNet.Length[] CoordinatesToNative(Geometry.Point point)
        {
            var unitString = point.units;
            return new UnitsNet.Length[]
            {
                GetUnitsNetLength(point.x, unitString),
                GetUnitsNetLength(point.y, unitString),
                GetUnitsNetLength(point.z, unitString)
            };
        }

        private StructuralPointConnection GetNodeAtPoint(Geometry.Point point)
        {
            return AdmModel.OfType<StructuralPointConnection>().FirstOrDefault(n => NodesCoincide(n, point));
        }

        private StructuralPointConnection GetNodeByName(string name)
        {
            return AdmModel.OfType<StructuralPointConnection>().FirstOrDefault(n => n.Name == name);
        }
        #endregion


        #region -- UNITS --
        private bool LengthEquals(UnitsNet.Length a, UnitsNet.Length b)
        {
            return Math.Abs(a.Meters - b.Meters) <= CoincidenceTolerance;
        }

        private bool IsAlmostZero(double a)
        {
            return Math.Abs(a) <= 1e-9;
        }

        private UnitsNet.Length GetUnitsNetLength(double value, string unit)
        {
            // Units.GetUnitsFromString(unit) > conversion that should be (and in case of Grasshopper Connecter is) applied when creating the Speckle Base
            switch (unit)
            {
                case null:
                    return UnitsNet.Length.FromMeters(value);
                case Units.Meters:
                    return UnitsNet.Length.FromMeters(value);
                case Units.Centimeters:
                    return UnitsNet.Length.FromCentimeters(value);
                case Units.Millimeters:
                    return UnitsNet.Length.FromMillimeters(value);
                case Units.Kilometers:
                    return UnitsNet.Length.FromKilometers(value);
                case Units.Feet:
                    return UnitsNet.Length.FromFeet(value);
                case Units.Inches:
                    return UnitsNet.Length.FromInches(value);
                case Units.Yards:
                    return UnitsNet.Length.FromYards(value);
                case Units.Miles:
                    return UnitsNet.Length.FromMiles(value);

                default:
                    try
                    {
                        return UnitsNet.Length.Parse($"{value} {unit}");
                    }
                    catch 
                    {
                        throw new ArgumentException(nameof(unit));
                    }
            }
        }
        #endregion


        #region -- ERROR HANDLING --
        /*
        private static Exception HandleErrorResult(ResultOfPartialAddToAnalysisModel addResult)
        {
            switch (addResult.PartialAddResult.Status)
            {
                case AdmChangeStatus.InvalidInput:
                    throw new Exception(addResult.PartialAddResult.Warnings);
                case AdmChangeStatus.Error:
                    throw new Exception(addResult.PartialAddResult.Errors);
                case AdmChangeStatus.NotDone:
                    throw new Exception(addResult.PartialAddResult.Warnings);
            }
            if (addResult.PartialAddResult.Exception != null)
            {
                throw addResult.PartialAddResult.Exception;
            }
            throw new Exception("Unknown ADM Error");
        }*/
        #endregion

    }

    public static class GeometryExtensions
    {
        #region -- GEOMETRY -- 

        #region --- CURVES ---
        public static Geometry.Vector GetDirection(this ICurve curve, bool normalized = true)
        {
            double x, y, z;
            switch (curve)
            {
                case Geometry.Line line:
                    x = line.end.x - line.start.x;
                    y = line.end.y - line.start.y;
                    z = line.end.z - line.start.z;
                    break;
                default:
                    throw new NotImplementedException($"Curve type {curve.GetType()} not implemented");
            }

            var vector = new Geometry.Vector(x, y, z);

            if (normalized)
                vector.Normalize();

            return vector;
        }


        /// <summary>
        /// Find the parameter of the point on an infinite line
        /// (by a start point and direction) closest to a given point.
        /// </summary>
        public static double ParameterOfClosestPointOnLine(
            Point point, Point lineStart, Vector lineDirection
        )
        {
            return Vector.DotProduct(new Vector(point - lineStart), lineDirection) /
                Math.Pow(lineDirection.Length, 2);
        }
        /*
        /// <summary>
        /// Find the point on an infinite line (by a start point and direction)
        /// closest to a given point.
        /// </summary>
        public static Point ClosestPointOnLine(
            Point point, Point lineStart, Vector lineDirection
        )
        {
            double t = ParameterOfClosestPointOnLine(point, lineStart, lineDirection);
            return lineStart + lineDirection * t;
        }*/

        /// <summary>
        /// Find the distance between a point and an infinite line
        /// (by a start point and direction).
        /// </summary>
        public static double DistancePointToLine(
            Point point, Point lineStart, Vector lineDirection
        )
        {
            return (Vector.CrossProduct(lineDirection, new Vector(lineStart - point))).Length / lineDirection.Length;
        }
        /// <summary>
        /// Find the parameter of the point on line segment
        /// (by a start and end point) closest to a given point.
        /// </summary>
        public static double ParameterOfClosestPointOnSegment(
            Point point, Point lineStart, Point lineEnd
        )
        {
            double t = ParameterOfClosestPointOnLine(point, lineStart, new Vector(lineEnd - lineStart));
            if (t < 0)
            {
                return 0;
            }
            if (t > 1)
            {
                return 1;
            }
            return t;
        }
        /// <summary>
        /// Find the distance between a point and a line segment
        /// (by a start and end point).
        /// </summary>
        public static double DistancePointToSegment(
            Point point, Point lineStart, Point lineEnd
        )
        {
            Vector lineDirection = new Vector(lineEnd - lineStart);
            double t = ParameterOfClosestPointOnLine(point, lineStart, lineDirection);
            if (t < 0)
            {
                return Point.Distance(lineStart, point);
            }
            if (t > 1)
            {
                return Point.Distance(lineEnd, point);
            }
            return DistancePointToLine(point, lineStart, lineDirection);
        }
        #endregion

        #endregion
    }
}
