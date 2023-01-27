using System;
using System.Collections.Generic;
using System.Text;

namespace Objects.Structural.SCIA
{
    /// <summary>
    /// Supported shapes in SAF for cross sections.
    /// See https://www.saf.guide/en/stable/annexes/supported-shapes-of-parametric-cross-section.html
    /// or https://docs.calatrava.scia.net/html/b7982386-50c5-bbfd-7e63-e7e58d91c11b.htm
    /// </summary>
    public enum SCIAProfileLibraryId
    {
        Circle,
        Rectangle,
        DoubleRectangle,
        TripleRectangle,
        RectangleWithPlates,
        DoubleRectangleWithPlates,
        ISection,
        ISectionWithHaunch,
        TSection,
        CSection,
        LSection,
        LSectionOpposite,
        USection,
        Oval,
        Pipe,
        Polygon,
        XSection,
        ZSection,
        Box,
        DoubleBox,
        IRolled,
        IRolledAsymmetric,
        Tube,
        Angle,
        Channel,
        TTee,
        ZZee,
        ColdFormedChannel,
        ColdFormedChannelWithLips,
        ColdFormedZee,
        Trapezoid,
        ISectionWithHaunchAsymmetric,
        TSectionWithHaunch
    }
}
