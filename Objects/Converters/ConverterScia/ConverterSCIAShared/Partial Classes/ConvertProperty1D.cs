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


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---

        public StructuralCrossSection Property1DToNative(Structural.Properties.Property1D speckleProperty1D)
        {
            if (ExistsInContext(speckleProperty1D, out IEnumerable<StructuralCrossSection> contextObjects))
                return contextObjects.FirstOrDefault();

            var speckleProfile = speckleProperty1D.profile ?? throw new ArgumentNullException("Property1D.profile");
            var admMaterial = MaterialToNative(speckleProperty1D.material ?? throw new ArgumentNullException("Property1D.material"));

            StructuralCrossSection admCrossSection;
            Guid guid = GetSCIAId(speckleProperty1D);

            // Manufactured/catalogue cross sections
            if (speckleProfile is Structural.Properties.Profiles.Catalogue catCS)
            {
                if (!Enum.TryParse(catCS.sectionType, out FormCode formCode)) // see https://www.saf.guide/en/stable/annexes/formcodes.html
                {
                    throw new ArgumentException("Profile SectionType is not a valid FormCode");
                }

                // The catalogue profile's deiscriptionId can come from either the catalogueName or the description property
                string descriptionString = string.IsNullOrWhiteSpace(catCS.catalogueName) ? catCS.description : catCS.catalogueName;
                if (!Enum.TryParse(descriptionString, out DescriptionId descriptionId))
                {
                    throw new ArgumentException("Profile Description is not a valid DescriptionId");
                }

                // The catalogue profile's name can come from either the name or the sectionName property
                bool nameMissing = string.IsNullOrWhiteSpace(catCS.name);
                bool sectionNameMissing = !string.IsNullOrWhiteSpace(catCS.sectionName);

                if (nameMissing && sectionNameMissing)
                    throw new ArgumentNullException("name+sectionName", "Catalogue profile properties can't all be empty");
                
                string name = nameMissing ? catCS.name: catCS.sectionName;
                string sectionName = sectionNameMissing ? catCS.sectionName : catCS.name;

                admCrossSection = new StructuralManufacturedCrossSection(guid, name, admMaterial, sectionName, formCode, descriptionId);
            }

            // Parametric cross sections
            // see https://www.saf.guide/en/stable/annexes/supported-shapes-of-parametric-cross-section.html
            // or https://help.scia.net/17.0/nl/pvt/profile_library_editor/annex_a_profile_library_formcodes.htm
            else
            {
                ProfileLibraryId profileLibId;
                IList<double> dimensions;

                switch (speckleProfile)
                {
                    // Rectangles and Boxes
                    case Structural.Properties.Profiles.Rectangular rect:
                        if (rect.webThickness > 0 && rect.flangeThickness > 0)
                        {
                            profileLibId = ProfileLibraryId.Box;
                            dimensions = new double[] { rect.width, rect.webThickness, rect.depth, rect.flangeThickness, rect.flangeThickness };  // width x t web x height x t bottom flange x t top flange
                        }
                        else
                        {
                            profileLibId = ProfileLibraryId.Rectangle;
                            dimensions = new double[] { rect.depth, rect.width };  // height x width
                        }
                        break;

                    // Circular and Pipes
                    case Structural.Properties.Profiles.Circular circ:
                        if (circ.wallThickness > 0)
                        {
                            profileLibId = ProfileLibraryId.Pipe;
                            dimensions = new double[] { 2 * circ.radius, circ.wallThickness };  // diameter x thickness
                        }
                        else
                        {
                            profileLibId = ProfileLibraryId.Circle;
                            dimensions = new double[] { 2 * circ.radius };  // diameter
                        }
                        break;

                    // L-shape
                    case Structural.Properties.Profiles.Angle angle:
                        profileLibId = angle.depth < 0 ? ProfileLibraryId.LSectionOpposite : ProfileLibraryId.LSection;
                        dimensions = new double[] { Math.Abs(angle.depth), Math.Abs(angle.width), angle.flangeThickness, angle.webThickness }; // height x width x t base x t web
                        break;

                    // I-shape
                    case Structural.Properties.Profiles.ISection ip:
                        profileLibId = ProfileLibraryId.ISection;
                        dimensions = new double[] { ip.depth, ip.width, ip.width, ip.flangeThickness, ip.flangeThickness, ip.webThickness };  // height x w top flange x w bottom flange x t bottom flange x t top flange x t web
                        break;

                    case Structural.SCIA.Properties.Profiles.SCIAParametricProfile pcs:
                        profileLibId = (ProfileLibraryId)Enum.Parse(typeof(ProfileLibraryId), pcs.profileId.ToString());
                        dimensions = pcs.parameters;
                        break;

                    default:
                        // TODO: add additional cross-section support
                        throw new NotImplementedException();
                }

                admCrossSection = new StructuralParametricCrossSection(guid,
                    speckleProfile.name ?? speckleProperty1D.name ?? $"{profileLibId}{string.Join("x", dimensions)}",
                    admMaterial, profileLibId,
                    dimensions.Select(d => GetUnitsNetLength(d, speckleProfile.units)).ToArray());
                
                // Todo add advanced cross section properties
            }

            AddToAnalysisModel(admCrossSection, speckleProperty1D);

            return admCrossSection;
        }

        #endregion


        #region --- TO SPECKLE ---

        public Structural.Properties.Property1D CrossSectionToSpeckle(StructuralCrossSection admCrossSection)
        {
            // Check whether the element already exists in the current context, and if so, return it
            if (ExistsInContext(admCrossSection, out Structural.Properties.Property1D contextObject))
                return contextObject;

            var speckleMaterial = MaterialToSpeckle(admCrossSection.Material);

            Structural.Properties.Profiles.SectionProfile speckleProfile;

            // Manufactured cross sections
            if (admCrossSection is StructuralManufacturedCrossSection catalogueSection) //Structural.Properties.Profiles.Catalogue cat)
            {
                speckleProfile = new Structural.Properties.Profiles.Catalogue()
                {
                    sectionType = catalogueSection.FormCode.ToString(),
                    description = catalogueSection.DescriptionId.ToString(),
                    sectionName = catalogueSection.Profile,
                    shapeType = Structural.ShapeType.Catalogue,
                };
            }

            // Parametric cross sections
            // see https://www.saf.guide/en/stable/annexes/supported-shapes-of-parametric-cross-section.html
            // or https://help.scia.net/17.0/nl/pvt/profile_library_editor/annex_a_profile_library_formcodes.htm
            else if (admCrossSection is StructuralParametricCrossSection parametricSection)
            {
                // TODO set correct unit
                var dimensions = parametricSection.Parameters.Select(v => v.Millimeters).ToArray();

                speckleProfile = parametricSection.Shape switch
                {
                    ProfileLibraryId.Box => new Structural.Properties.Profiles.Rectangular()
                    {
                        width = dimensions[0],
                        webThickness = dimensions[1],
                        depth = dimensions[2],
                        flangeThickness = dimensions[3],
                        shapeType = Structural.ShapeType.Rectangular,
                    },
                    ProfileLibraryId.Rectangle => new Structural.Properties.Profiles.Rectangular()
                    {
                        depth = dimensions[0],
                        width = dimensions[1],
                        shapeType = Structural.ShapeType.Rectangular,
                    },
                    ProfileLibraryId.Pipe => new Structural.Properties.Profiles.Circular()
                    {
                        radius = dimensions[0] / 2,
                        wallThickness = dimensions[1],
                        shapeType = Structural.ShapeType.Circular,
                    },
                    ProfileLibraryId.Circle => new Structural.Properties.Profiles.Circular()
                    {
                        radius = dimensions[0] / 2,
                        shapeType = Structural.ShapeType.Circular,
                    },
                    ProfileLibraryId.LSection => new Structural.Properties.Profiles.Angle()
                    {
                        depth = dimensions[0],
                        width = dimensions[1],
                        flangeThickness = dimensions[2],
                        webThickness = dimensions[3],
                        shapeType = Structural.ShapeType.Angle,
                    },
                    ProfileLibraryId.LSectionOpposite => new Structural.Properties.Profiles.Angle()
                    {
                        depth = -dimensions[0],
                        width = dimensions[1],
                        flangeThickness = dimensions[2],
                        webThickness = dimensions[3],
                        shapeType = Structural.ShapeType.Angle,
                    },
                    ProfileLibraryId.ISection => new Structural.Properties.Profiles.ISection()
                    {
                        depth = dimensions[0],
                        width = dimensions[1],
                        flangeThickness = dimensions[3],
                        webThickness = dimensions[5],
                        shapeType = Structural.ShapeType.I,
                    },
                    _ => new Structural.SCIA.Properties.Profiles.SCIAParametricProfile()
                    { 
                        profileId = (Structural.SCIA.SCIAProfileLibraryId)Enum.Parse(typeof(Structural.SCIA.SCIAProfileLibraryId), parametricSection.Shape.ToString()),
                        parameters = dimensions.ToList(),
                        shapeType = Structural.ShapeType.Perimeter,  // TODO: should be 'Other' or 'Parametric' added to ShapeType enum
                    }
                    // throw new NotImplementedException($"{admCrossSection.Name}: No support for parametric shape {parametricSection.Shape}"),
                };
            }
            else
            {
                // TODO: add additional cross-section support
                throw new NotImplementedException($"{admCrossSection.Name}: No support for StructuralCrossSection of type {admCrossSection.GetType().Name}");
            }
            speckleProfile.name = admCrossSection.Name;
            speckleProfile.units = Units.Millimeters;

            // Adding advanced cross section properties
            var admCSProperties = admCrossSection.CrossSectionalProperties;
            if (admCSProperties != null)
            {
                if (admCSProperties.A.HasValue)
                    speckleProfile.area = admCSProperties.A.Value.SquareMeters;

                if (admCSProperties.Iy.HasValue)
                    speckleProfile.Iyy = admCSProperties.Iy.Value.MetersToTheFourth;

                if (admCSProperties.Iz.HasValue)
                    speckleProfile.Izz = admCSProperties.Iz.Value.MetersToTheFourth;

                if (admCSProperties.It.HasValue)
                    speckleProfile.J = admCSProperties.It.Value.MetersToTheFourth;
                /* SCIA CrossSectionalProperties:
                 * - Area A [m2]	
                 * - Moments of inertia Iy [m4] Iz [m4]
                 * - Torsion moment of inertia It [m4]
                 * - Warping constant Iw [m6]
                 * - Plastic section moduli Wply [m3] Wplz [m3]
                 * Speckle Profile properties: 
                 * - area 
                 * - Second moment of area Iyy Izz
                 * - Polar Moment of Inertia J (indication of a structural member's ability to resist torsion about an axis perpendicular to the section)
                 * - Radius of Gyration Ky  Kz */
            }

            var speckleProperty1D = new Structural.Properties.Property1D()
            {
                name = admCrossSection.Name,
                material = speckleMaterial,
                profile = speckleProfile,
            };

            AddToSpeckleModel(speckleProperty1D, admCrossSection);
            return speckleProperty1D;
        }
        #endregion

    }
}
