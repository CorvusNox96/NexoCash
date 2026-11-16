using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;
var path = Path.Combine(Path.GetTempPath(), "nexocash-cash-smoke-" + Guid.NewGuid().ToString("N") + ".db");
DatabaseInitializer.Initialize(path);
var accounts = new AccountRepository(path);
var cash = new CashRepository(path);
var first = accounts.Save(null, "Caja uno", AccountType.Cash);
var second = accounts.Save(null, "Caja dos", AccountType.Cash);
accounts.Record(first, "CUP", 15000m, true, new Dictionary<decimal,int>(), null);
if (cash.ListBalanceComparisons().Single(x => x.Currency == "CUP").CashBalance != 15000m) throw new Exception("balance");
var threeFiveThousands = new Dictionary<string,IReadOnlyDictionary<decimal,int>> { ["CUP"] = new Dictionary<decimal,int> { [5000m] = 3 } };
cash.Reconcile(threeFiveThousands);
if (cash.ListInventory().Single(x => x.Currency == "CUP" && x.Value == 5000m).Quantity != 3) throw new Exception("first count");
accounts.Record(first, "CUP", 5000m, false, new Dictionary<decimal,int> { [5000m] = 1 }, null);
if (cash.ListInventory().Single(x => x.Currency == "CUP" && x.Value == 5000m).Quantity != 2) throw new Exception("withdrawal");
try { cash.Reconcile(threeFiveThousands); throw new Exception("mismatch accepted"); }
catch (InvalidOperationException) { }
accounts.Record(second, "CUP", 5000m, true, new Dictionary<decimal,int>(), null);
cash.Reconcile(threeFiveThousands);
if (cash.ListInventory().Single(x => x.Currency == "CUP" && x.Value == 5000m).Quantity != 3) throw new Exception("global count");
using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM Movements";
if (Convert.ToInt32(command.ExecuteScalar()) != 3) throw new Exception("recount created money movement");
if (cash.ListBalanceComparisons().Single(x => x.Currency == "CUP").Difference != 0) throw new Exception("totals differ");
Console.WriteLine("Cash recount, rejection, later withdrawal, and global balance OK");
