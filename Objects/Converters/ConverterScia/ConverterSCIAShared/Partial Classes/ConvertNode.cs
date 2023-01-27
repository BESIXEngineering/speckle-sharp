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
using ModelExchanger.AnalysisDataModel.StructuralElements;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Curves;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Points;
using ModelExchanger.AnalysisDataModel.Subtypes;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects;
using Speckle.Core.Models;
using Speckle.Core.Kits;
using ModelExchanger.AnalysisDataModel.Interfaces;

namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {

        #region --- TO SCIA ---

        /// <summary>
        /// If no name is given, will return the existing node at the given location. 
        /// Else, will return the node with the given name and checks whether the location matches.
        /// New points are added to the global dictionary.
        /// </summary>
        /// <param name="specklePoint"></param>
        /// <param name="name">Optional target name of the node</param>
        /// <param name="targetGuid">Optional target SCIA ADM guid of the node</param>
        /// <returns></returns>
        public StructuralPointConnection PointToNative(Geometry.Point specklePoint, string name = null, Guid? targetGuid = null)
        {
            var key = name;
            // ToDo: merge coinciding node at the end of the conversion process? Or is this done automatically by the API?

            UnitsNet.Length[] coord = CoordinatesToNative(specklePoint);

            StructuralPointConnection sciaNode;
            Guid guid = targetGuid ?? Guid.NewGuid();

            // If no name given, return an existing node at the given location
            // or, if none existing, create a unique name for a new node
            if (string.IsNullOrWhiteSpace(key))
            {
                // Get node at location
                sciaNode = GetNodeAtPoint(specklePoint);
                if (sciaNode != null)
                {
                    return sciaNode;
                }
                // Create a unique name for the new node
                key = GetUniqueADMName<StructuralPointConnection>("N");
            }
            // If a name is specified, check whether a node with the given name already exists
            // and if so, validate that the coordinates match.
            else
            {
                sciaNode = GetNodeByName(key);
                if (sciaNode != null)
                {
                    if (NodesCoincide(sciaNode, coord))
                    {
                        return sciaNode;
                    }
                    else
                    {
                        // If the existing node exists in the current context, then the coordinates MUST MATCH;
                        if (ContextObjects.Any(placeholder => placeholder.Converted.OfType<StructuralPointConnection>().Any(n => n.Id == sciaNode.Id)))
                            throw new ArgumentException($"Node {key} already created with different coordinates");
                        // If not, create a new SCIA Node using this node's id;
                        guid = sciaNode.Id;
                    }
                }
            }

            sciaNode = new StructuralPointConnection(guid, key, coord[0], coord[1], coord[2]);

            AddToAnalysisModel(sciaNode, specklePoint);

            return sciaNode;
        }

        /// <summary>
        /// Creates a SCIA node, and if the info is available, a PointSupport at the node.
        /// </summary>
        /// <param name="speckleNode"></param>
        /// <returns></returns>
        public List<IAnalysisObject> NodeToNative(Structural.Geometry.Node speckleNode)
        {
            var sciaNode = PointToNative(speckleNode.basePoint, speckleNode.name, GetSCIAId(speckleNode));
            var sciaObjects = new List<IAnalysisObject>() { sciaNode };

            // Note: all restraints with the same code (e.g. FFFFFF) have the same applicationId, while in SCIA they should have different ids.
            //  Hence an identical SpeckleBase.applicationId does not guarentee a unique SCIA object!

            // TODO: applicationId from Grasshopper will not parse as a GUID, hence the GetSCIAId() method will each time return a unique Id. If however another app, that is not SCIA, generates unique Ids as applicationId, this might be an issue.
            //  Hence for conversion from SCIA to speckle, add a "SCIA" identifier to the Id to make sure only Ids orinating from SCIA are accepted by the GetSCIAId() method
            Structural.Geometry.Restraint speckleRestraint = speckleNode.restraint;
            
            if (speckleRestraint != null && speckleRestraint.code != SpeckleRestraintCodeFree) // ignore the restraint if the node is fully free
            {
                string name = GetSpeckleDynamicName(speckleRestraint) ?? $"S{sciaNode.Name}";
                StructuralPointSupport admPointSupport = GetAdmObjectByName<StructuralPointSupport>(name);

                if (admPointSupport == null)
                {
                    // TODO: add detailed restraint info
                    admPointSupport = new StructuralPointSupport(GetSCIAId(speckleRestraint), name, sciaNode);
                    SetADMRestraint(admPointSupport, speckleRestraint.code);
                    AddToAnalysisModel(admPointSupport, speckleRestraint);
                }
                
                sciaObjects.Add(admPointSupport);
            }

            return sciaObjects;
        }

        #endregion


        #region --- TO SPECKLE ---

        /// <summary>
        /// Creates a Speckle node from an ADM StructuralPointConnection.
        /// </summary>
        public Structural.Geometry.Node NodeToSpeckle(StructuralPointConnection admNode)
        {
            // Check whether the element already exists in the current context, and if so, return it
            if (ExistsInContext(admNode, out Structural.Geometry.Node contextObject))
                return contextObject;

            Structural.Geometry.Node speckleNode = new Structural.Geometry.Node()
            {
                name = admNode.Name,
                // ToDo: change according to unit settings
                basePoint = PointToSpeckle(admNode)
            };

            AddToSpeckleModel(speckleNode, admNode);
            return speckleNode;
        }

        /// <summary>
        /// Covert the XYZ coordinates stored in a ADM StructuralPointConnection to Speckle Point geometry.
        /// </summary>
        /// <param name="admPoint"></param>
        /// <returns></returns>
        public Geometry.Point PointToSpeckle(StructuralPointConnection admPoint) 
            => new Geometry.Point(admPoint.X.Meters, admPoint.Y.Meters, admPoint.Z.Meters, Units.Meters);


        /// <summary>
        /// Creates a Speckle restraint at a Node. 
        /// Note: A unique restraint will be created for each PointSupport, which is not how Speckle usually handles Restraint objects.
        /// </summary>
        /// <param name="admSupport"></param>
        /// <returns></returns>
        public Structural.Geometry.Restraint PointSupportToSpeckle(StructuralPointSupport admPointSupport)
        {
            Structural.Geometry.Node speckleNode = NodeToSpeckle(admPointSupport.Node);
            Structural.Geometry.Restraint speckleRestraint = speckleNode.restraint;

            // If the Speckle node already has a restraint assigned, then return that one
            if (speckleRestraint != null)
            {
                string name = GetSpeckleDynamicName(speckleRestraint) 
                    ?? throw new ArgumentException("Can't add restraint to node that contains an anonymous restraint");

                if (name != admPointSupport.Name) throw new ArgumentException("Can't add multiple restraints to node");
                return speckleRestraint;
            }

            /*switch (admPointSupport.Type)
            {
                case BoundaryNodeCondition.Fixed:
                    speckleRestraint = new Structural.Geometry.Restraint(Structural.Geometry.RestraintType.Fixed);
                    break;

                case BoundaryNodeCondition.Hinged:
                    speckleRestraint = new Structural.Geometry.Restraint(Structural.Geometry.RestraintType.Pinned);
                    break;

                case BoundaryNodeCondition.Sliding:
                    speckleRestraint = new Structural.Geometry.Restraint(Structural.Geometry.RestraintType.Roller);
                    break;

                default:
                    speckleRestraint = ConstraintToSpeckle(admPointSupport);
                    break;
            }*/
            speckleRestraint = ConstraintToSpeckle(admPointSupport);

            speckleRestraint.units = Units.Meters;
            // Though not natively supported in the Speckle object model, assign the SCIA name to the restraint object
            speckleNode.restraint = speckleRestraint;

            return speckleRestraint;
        }
        #endregion
    }
}
