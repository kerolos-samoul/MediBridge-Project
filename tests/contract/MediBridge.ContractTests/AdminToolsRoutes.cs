namespace MediBridge.ContractTests;

public static class AdminToolsRoutes
{
    public const string WorkQueue = "/api/admin/work-queue";
    public const string DoctorWithdrawals = "/api/doctor/withdrawals";
    public const string AdminWithdrawals = "/api/admin/withdrawals";
    public const string AdminStatistics = "/api/admin/statistics";

    public static string ApproveWithdrawal(string withdrawalId) => $"/api/admin/withdrawals/{withdrawalId}/approve";

    public static string RejectWithdrawal(string withdrawalId) => $"/api/admin/withdrawals/{withdrawalId}/reject";

    public static string MarkWithdrawalPaid(string withdrawalId) => $"/api/admin/withdrawals/{withdrawalId}/mark-paid";

    public static string MarkWithdrawalFailed(string withdrawalId) => $"/api/admin/withdrawals/{withdrawalId}/mark-failed";

    public static string DeactivateDoctorPricing(string doctorId) => $"/api/admin/doctors/{doctorId}/price/deactivate";
}
