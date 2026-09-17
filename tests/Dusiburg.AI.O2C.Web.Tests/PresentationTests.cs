using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;

namespace Dusiburg.AI.O2C.Web.Tests;

/// <summary>Regole di presentazione delle due UI: chiusura, refresh automatico (G6.7) ed evidenza del magazzino.</summary>
public class PresentationTests
{
    [TestCase(DealStage.ContractSent, null, true, false)]
    [TestCase(DealStage.ClosedWon, null, false, true)]
    [TestCase(DealStage.ClosedWon, DealStatus.ApprovalPending, false, true)]
    [TestCase(DealStage.ClosedWon, DealStatus.OrderCreated, false, false)]
    [TestCase(DealStage.ClosedWon, DealStatus.Rejected, false, false)]
    [TestCase(DealStage.ClosedWon, DealStatus.Expired, false, false)]
    [TestCase(DealStage.ClosedWon, DealStatus.Discarded, false, false)]
    [TestCase(DealStage.ClosedWon, DealStatus.Failed, false, false)]
    [TestCase(DealStage.ClosedLost, null, false, false)]
    public void Deal_CanCloseAndIsInFlight_FollowStageAndO2CStatus(DealStage stage, DealStatus? status, bool canClose, bool inFlight)
    {
        //SETUP
        var deal = new DealSummaryView("D-1001", "Deal", "C-01", "Azienda", 100m, "EUR", stage, 1, status, null, DateTimeOffset.UtcNow);

        //SUT
        Assert.That((DealPresentation.CanClose(deal), DealPresentation.IsInFlight(deal)), Is.EqualTo((canClose, inFlight)));
    }

    [TestCase(10, 2, "")]
    [TestCase(10, 10, "table-warning")]
    [TestCase(0, 0, "table-warning")]
    [TestCase(3, 5, "table-danger")]
    public void Stock_RowClass_MarksShortAndOverReserved(int onHand, int reserved, string expected)
    {
        //SETUP
        var item = new StockItemView("IND-MOT-003", "Motore", "PZ", 845m, onHand, reserved, onHand - reserved, 21);

        //SUT
        Assert.That(ErpPresentation.StockRowClass(item), Is.EqualTo(expected));
    }
}
