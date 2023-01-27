using Speckle.Core.Kits;
using Speckle.Core.Models;
using System.Collections.Generic;
using Objects.Geometry;
using Objects.Structural.Geometry;
using Objects.Structural.Properties;

namespace Objects.Structural.SCIA.Geometry
{
    public class SCIAElement1D : Element1D
    {
        public string layer { get; set; }

        public SCIAElement1D() { }

        [SchemaInfo("SCIAElement1D (from local axis)", "Creates a Speckle structural 1D element for SCIA (from local axis)", "SCIA", "Geometry")]
        public SCIAElement1D(Line baseLine, Property1D property, ElementType1D type, string name = null,
            [SchemaParamInfo("If null, restraint condition defaults to unreleased (fully fixed translations and rotations)")] Restraint end1Releases = null,
            [SchemaParamInfo("If null, restraint condition defaults to unreleased (fully fixed translations and rotations)")] Restraint end2Releases = null,
            [SchemaParamInfo("If null, defaults to no offsets")] Vector end1Offset = null,
            [SchemaParamInfo("If null, defaults to no offsets")] Vector end2Offset = null, Plane localAxis = null,
            string layer = null)
        {
            this.baseLine = baseLine;
            this.property = property;
            this.type = type;
            this.name = name;
            this.end1Releases = end1Releases == null ? new Restraint("FFFFFF") : end1Releases;
            this.end2Releases = end2Releases == null ? new Restraint("FFFFFF") : end2Releases;
            this.end1Offset = end1Offset == null ? new Vector(0, 0, 0) : end1Offset;
            this.end2Offset = end2Offset == null ? new Vector(0, 0, 0) : end2Offset;
            this.localAxis = localAxis;
            this.layer = layer;
        }

        [SchemaInfo("SCIARigidLink", "Creates a Speckle 1D rigid link element for SCIA", "SCIA", "Geometry")]
        public SCIAElement1D(Line baseLine, string name = null,
            bool hingeAtStart = false, bool hingeAtEnd = false)
        {
            this.baseLine = baseLine;
            this.name = name;
            this.property = null;
            this.type = ElementType1D.Link;
            this.end1Releases = hingeAtStart ? new Restraint(RestraintType.Pinned) : new Restraint(RestraintType.Fixed);
            this.end2Releases = hingeAtEnd ? new Restraint(RestraintType.Pinned) : new Restraint(RestraintType.Fixed);
        }
    }
}
