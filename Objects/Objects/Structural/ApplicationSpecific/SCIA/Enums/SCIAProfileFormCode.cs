using System;
using System.Collections.Generic;
using System.Text;

namespace Objects.Structural.SCIA
{
    /// <summary>
    /// SAF form codes for catalogue cross sections.
    /// See https://www.saf.guide/en/stable/annexes/formcodes.html
    /// or https://docs.calatrava.scia.net/html/7667a972-be4d-96ad-9d9f-f9a329eb1d45.htm
    /// </summary>
    public enum SCIAProfileFormCode
    {
        // NotUsed,
        ISection,
        RectangularHollowSection,
        CircularHollowSection,
        LSection,
        ChannelSection,
        TSection,
        FullRectangularSection,
        FullCircularSection,
        TSectionFlipped,
        LSectionMirrored,
        LSectionFlipped,
        LSectionMirrorFlipped,
        ChannelSectionMirrored,
        AsymmetricISection,
        AsymmetricISectionFlipped,
        ZSection,
        ZSectionMirrored,
        OmegaSection,
        OmegaSectionFlipped,
        SigmaSection,
        SigmaSectionMirrored,
        SigmaSectionFlipped,
        SigmaSectionMirroredFlipped
    }
}