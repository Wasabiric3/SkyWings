using SkyWings;
using SkyWings.Email;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class BoardingPassDataTests
{
    private static BookingRecord Booking(bool roundTrip = true)
        => new("SKY-C1B9CCF52E7A", new DateTime(2026, 11, 18, 9, 0, 0), "PR-101", "Manila (MNL)", "Cebu (CEB)",
            new DateTime(2026, 12, 20), true, "First Class", "Credit Card", "**** 4521", 9000m, "Scheduled",
            new[] { new PassengerRecord("Sample2", "1 St", "0917", 30, "1996-01-01", "ID", false, false,
                new SeatAssignment("Sample2", "FST4A"), Array.Empty<string>(), 9000m) },
            AppData.StatusConfirmed, "PAID", "TXN-1", new DateTime(2026, 11, 18, 9, 0, 0), 0m)
        {
            ReturnFlightNo = roundTrip ? "PR-102" : null, ReturnOrigin = roundTrip ? "Cebu (CEB)" : null,
            ReturnDestination = roundTrip ? "Manila (MNL)" : null, ReturnFlightDateValue = roundTrip ? new DateTime(2026, 12, 27) : null
        };

    private static readonly CheckInRecord Record = new("SKY-C1B9CCF52E7A", "Sample2", "PR-101", new DateTime(2026, 12, 20), "FST4A",
        new DateTime(2026, 11, 18, 10, 15, 0));

    [TestMethod]
    public void From_OutboundLeg_MapsRouteFlightSeatAndTimes()
    {
        BoardingPassData d = BoardingPassData.From(Booking(), returnLeg: false, Record);

        Assert.AreEqual("SKY-C1B9CCF52E7A", d.Reference);
        Assert.AreEqual("Sample2", d.PassengerName);
        Assert.AreEqual(("MNL", "Manila"), (d.OriginCode, d.OriginCity));
        Assert.AreEqual(("CEB", "Cebu"), (d.DestinationCode, d.DestinationCity));
        Assert.AreEqual("PR-101", d.FlightNo);
        Assert.AreEqual("FST4A", d.Seat);
        Assert.AreEqual("First Class", d.ClassName);
        Assert.AreEqual(AppData.DepartureOf("PR-101", new DateTime(2026, 12, 20)), d.Departure);
        Assert.AreEqual(Record.CheckedInAt, d.CheckedInAt);
    }

    [TestMethod]
    public void From_ReturnLeg_UsesTheReturnRoute()
    {
        var back = Record with { FlightNo = "PR-102", FlightDate = new DateTime(2026, 12, 27) };
        BoardingPassData d = BoardingPassData.From(Booking(), returnLeg: true, back);

        Assert.AreEqual("CEB", d.OriginCode);
        Assert.AreEqual("MNL", d.DestinationCode);
        Assert.AreEqual("PR-102", d.FlightNo);
    }
}

[TestClass]
[DoNotParallelize]
public class BoardingPassPrinterTests
{
    private sealed class ThrowingQr : IQrRenderer
    {
        public byte[] RenderPng(string text, int pixelsPerModule = 8) => throw new NotSupportedException("no QR package");
    }

    /// <summary>A solid pure-red square standing in for a QR code, so its position on the ticket can be found by colour.</summary>
    private sealed class RedQr : IQrRenderer
    {
        public byte[] RenderPng(string text, int pixelsPerModule = 8)
        {
            using var bmp = new Bitmap(21, 21);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Red);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
    }

    private static BoardingPassData Pass(string name = "Patweak Malakas", string cls = "Business") => new(
        "SKY-C1B9CCF52E7A", name, "MNL", "Manila", "NRT", "Tokyo Narita", "SW328",
        new DateTime(2026, 11, 20, 9, 15, 0), cls, "01B", new DateTime(2026, 11, 19, 10, 15, 0));

    private static Bitmap Render(BoardingPassData data, IQrRenderer qr)
    {
        var bmp = new Bitmap(BoardingPassPrinter.TicketWidth, BoardingPassPrinter.TicketHeight);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        BoardingPassPrinter.DrawTicket(g, data, qr);
        return bmp;
    }

    [TestMethod]
    public void Ticket_HasTheKitsThreeSurfaces_PageTintCardAndHeaderBand()
    {
        using Bitmap t = Render(Pass(), new ThrowingQr());
        Assert.AreEqual(Color.FromArgb(0xED, 0xF2, 0xEE).ToArgb(), t.GetPixel(12, 215).ToArgb(), "tinted backdrop around the card");
        Assert.AreEqual(Color.FromArgb(0xF7, 0xFA, 0xF7).ToArgb(), t.GetPixel(600, 40).ToArgb(), "header band");
        Assert.AreEqual(Color.White.ToArgb(), t.GetPixel(600, 200).ToArgb(), "white card body");
    }

    [TestMethod]
    public void RealQr_WhenTheRendererWorks_IsDrawnInsideTheQrPanel()
    {
        using Bitmap t = Render(Pass(), new RedQr());
        Assert.AreEqual(Color.Red.ToArgb(), t.GetPixel(859, 215).ToArgb(), "the renderer's image fills the QR panel");
        Assert.AreEqual(Color.White.ToArgb(), t.GetPixel(798, 154).ToArgb(), "a white quiet margin surrounds it");
    }

    [TestMethod]
    public void WhenTheQrRendererIsUnavailable_ADecorativePatternIsDrawnInstead_WithoutThrowing()
    {
        using Bitmap t = Render(Pass(), new ThrowingQr());
        int dark = 0;
        for (int x = 801; x < 917; x++)
            for (int y = 157; y < 273; y++)
                if (t.GetPixel(x, y).R < 80) dark++;
        Assert.IsGreaterThan(300, dark, "a bar pattern should fill the panel");
    }

    [TestMethod]
    public void LongNamesAndClasses_DoNotThrow()
    {
        using Bitmap t = Render(Pass(new string('W', 120), "Premium Economy Plus Deluxe Elite"), new ThrowingQr());
        Assert.AreEqual(Color.White.ToArgb(), t.GetPixel(600, 200).ToArgb());
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(3, 1)]
    [DataRow(4, 2)]
    [DataRow(7, 3)]
    public void Printing_PutsThreePassesOnEachA4Page(int passes, int pages)
    {
        if (System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count == 0) Assert.Inconclusive("No printer installed.");
        var controller = new System.Drawing.Printing.PreviewPrintController();
        using var document = BoardingPassPrinter.CreateDocument(Enumerable.Repeat(Pass(), passes).ToList(), new ThrowingQr());
        document.PrintController = controller;

        document.Print();

        Assert.HasCount(pages, controller.GetPreviewPageInfo());
    }
}
