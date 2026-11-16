using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

var path = Path.Combine(Path.GetTempPath(), $"nexocash-flow-{Guid.NewGuid():N}.db");
try
{
    DatabaseInitializer.Initialize(path);
    DatabaseInitializer.SetStartDate(path, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
    DatabaseInitializer.Activate(path);
    var accounts = new AccountRepository(path);
    var accountId = accounts.Save(null, "Cuenta Personal", AccountType.Virtual);
    accounts.Record(accountId, "CUP", 1000m, true, new Dictionary<decimal, int>(), "Prueba");
    var agency = new Agency { Name = "Agencia de prueba", CurrentCommissionPercent = 10m };
    var agencies = new AgencyRepository();
    agencies.Create(agency, 0m);
    agencies.TopUp(agency.Id, accountId, "TRX-A12", 400m, 5m, new Dictionary<decimal, int>());
    var balance = accounts.ListActive().Single(item => item.Account.Id == accountId).Balances.Single(item => item.Currency == "CUP").Amount;
    if (balance != 580m) throw new Exception($"Saldo incorrecto: {balance}");
    var fund = agencies.ListActive().Single(item => item.Agency.Id == agency.Id).CupFund;
    if (fund != 400m) throw new Exception($"Fondo incorrecto: {fund}");
    var history = agencies.ListTopUps(agency.Id).Single();
    if (history.TransactionNumber != "TRX-A12" || history.DebitedAmount != 420m || history.SurchargePercent != 5m)
        throw new Exception("Historial incorrecto");
    try
    {
        agencies.TopUp(agency.Id, accountId, "TRX-A12", 10m, 0m, new Dictionary<decimal, int>());
        throw new Exception("Se aceptó una transacción duplicada");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("transacción")) { }
    if (agencies.ListActive().Single(item => item.Agency.Id == agency.Id).CupFund != 400m)
        throw new Exception("La operación duplicada cambió el fondo");
    var cashId = accounts.Save(null, "Caja", AccountType.Cash);
    accounts.Record(cashId, "CUP", 300m, true, new Dictionary<decimal, int>(), "Prueba");
    agencies.TopUp(agency.Id, cashId, "EF-02", 100m, 0m, new Dictionary<decimal, int>());
    if (accounts.ListActive().Single(item => item.Account.Id == cashId).Balances.Single(item => item.Currency == "CUP").Amount != 200m) throw new Exception("Retiro de efectivo incorrecto");
    if (agencies.ListActive().Single(item => item.Agency.Id == agency.Id).CupFund != 500m) throw new Exception("Abono de efectivo incorrecto");
    Console.WriteLine("Flujo de recarga OK");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (File.Exists(path)) File.Delete(path);
}



