using System;
using System.Collections.Generic;
using System.Text;

using Speckle.Core.Kits;
using Objects.Structural.Properties.Profiles;
using System.Linq;

namespace Objects.Structural.SCIA.Properties.Profiles
{
    public class SCIAParametricProfile: SectionProfile
    {
        public SCIAProfileLibraryId profileId { get; set; }
        public List<double> parameters { get; set; }

        public SCIAParametricProfile() { }

        [SchemaInfo("Parametric", "Creates a Speckle structural parametric section profile for SCIA", "SCIA", "Section Profile")]
        public SCIAParametricProfile(
            string name, 
            [SchemaParamInfo("ProfileId of SCIA ADM, see https://docs.calatrava.scia.net/html/b7982386-50c5-bbfd-7e63-e7e58d91c11b.htm")] SCIAProfileLibraryId profileId, 
            [SchemaParamInfo("Dimensions matching ProfileId, see https://www.saf.guide/en/stable/annexes/supported-shapes-of-parametric-cross-section.html")]  List<double> parameters)
        {
            this.name = name;
            this.profileId = profileId;
            this.parameters = parameters;
            this.shapeType = ShapeType.Perimeter;  // TODO: should be 'Other' or 'Parametric' added to ShapeType enum
    }
    }

    public class SCIACatalogueProfile : Catalogue
    {
        // public SCIAProfileFormCode formCode { get; set; }
        // public SCIAProfileDescription descriptionId { get; set; }

        public SCIACatalogueProfile() { }

        [SchemaInfo("Catalogue", "Creates a Speckle structural catalogue section profile for SCIA", "SCIA", "Section Profile")]
        public SCIACatalogueProfile(
            string name,
            [SchemaParamInfo("DescriptionId of SCIA ADM, see https://docs.calatrava.scia.net/html/542ff05d-6a64-4436-90c1-fdb43f13e1ae.htm")] SCIAProfileDescriptionId catalogueName,
            [SchemaParamInfo("FormCode of SCIA ADM, see https://docs.calatrava.scia.net/html/7667a972-be4d-96ad-9d9f-f9a329eb1d45.htm")] SCIAProfileFormCode sectionType,
            [SchemaParamInfo("Section Name of as mentioned in the catalogue")] string sectionName)
        {
            this.name = name;
            this.sectionName = sectionName;

            this.catalogueName = catalogueName.ToString();
            this.sectionType = sectionType.ToString();

            if (!GetValidDescriptionIds(sectionType).Contains(catalogueName))
            {
                throw new ArgumentException($"Valid catalogues for section type {sectionType}:\n{(string.Join(", ", GetValidDescriptionIds(sectionType)))}");
            }
        }

        private IEnumerable<SCIAProfileDescriptionId> GetValidDescriptionIds(SCIAProfileFormCode formcode)
        {
            // Enum integers copied from https://www.saf.guide/en/stable/annexes/formcodes.html
            int[] options;
            switch (formcode)
            {
                case SCIAProfileFormCode.ISection:
                    options = new int[] { 1, 2, 3, 4, 5, 11, 12, 13, 29, 33, 34, 35, 36, 37, 38, 39, 40, 55, 63, 64, 68, 69, 71, 72, 91, 96, 102, 105, 106, 109, 110 };
                    break;
                case SCIAProfileFormCode.RectangularHollowSection:
                    options = new int[] { 25, 27, 49, 50, 74, 75, 82, 83, 84, 85, 86, 87, 93, 94 };
                    break;
                case SCIAProfileFormCode.CircularHollowSection:
                    options = new int[] { 20, 21, 51, 52, 53, 54,88, 89, 90, 95, 103 };
                    break;
                case SCIAProfileFormCode.LSection:
                    options = new int[] { 8, 9, 10, 16, 45, 46, 65, 73, 98, 99};
                    break;
                case SCIAProfileFormCode.ChannelSection:
                    options = new int[] { 6, 7, 15, 18, 28, 41, 42, 43, 44, 57, 62, 66, 67, 70, 76, 97, 101, 107, 108, 111, 112, 113, 114, 115, 118 };
                    break;
                case SCIAProfileFormCode.TSection:
                    options = new int[] { 23, 31, 47, 48, 77, 78, 80, 81, 92 };
                    break;
                case SCIAProfileFormCode.FullRectangularSection:
                    options = new int[] { 14, 22, 30, 79 };
                    break;
                case SCIAProfileFormCode.FullCircularSection:
                    options = new int[] { 26 };
                    break;
                case SCIAProfileFormCode.TSectionFlipped:
                    options = new int[] { 23, 31, 47, 48, 77, 78, 80, 81, 92 };
                    break;
                case SCIAProfileFormCode.LSectionMirrored:
                    options = new int[] { 8, 9, 10, 16, 45, 46, 65, 73, 98, 99 };
                    break;
                case SCIAProfileFormCode.LSectionFlipped:
                    options = new int[] { 8, 9, 10, 16, 45, 46, 65, 73, 98, 99 };
                    break;
                case SCIAProfileFormCode.LSectionMirrorFlipped:
                    options = new int[] { 8, 9, 10, 16, 45, 46, 65, 73, 98, 99 };
                    break;
                case SCIAProfileFormCode.ChannelSectionMirrored:
                    options = new int[] { 6, 7, 15, 18, 28, 41, 42, 43, 44, 57, 62, 66, 67, 70, 76, 97, 101, 107, 108, 111, 112, 113, 114, 115, 118 };
                    break;
                case SCIAProfileFormCode.AsymmetricISection:
                    options = new int[] { 24 };
                    break;
                case SCIAProfileFormCode.AsymmetricISectionFlipped:
                    options = new int[] { 24 };
                    break;
                case SCIAProfileFormCode.ZSection:
                    options = new int[] { 19, 32, 56, 100 };
                    break;
                case SCIAProfileFormCode.ZSectionMirrored:
                    options = new int[] { 19, 32, 56, 100 };
                    break;
                case SCIAProfileFormCode.OmegaSection:
                    options = new int[] { 17 };
                    break;
                case SCIAProfileFormCode.OmegaSectionFlipped:
                    options = new int[] { 17 };
                    break;
                case SCIAProfileFormCode.SigmaSection:
                    options = new int[] { };
                    break;
                case SCIAProfileFormCode.SigmaSectionMirrored:
                    options = new int[] { 58, 104 };
                    break;
                case SCIAProfileFormCode.SigmaSectionFlipped:
                    options = new int[] { 58, 104 };
                    break;
                case SCIAProfileFormCode.SigmaSectionMirroredFlipped:
                    options = new int[] { 58, 104 };
                    break;
                default:
                    throw new NotImplementedException();
            }
            return options.Select(x => (SCIAProfileDescriptionId)x);
        }
    }
}
