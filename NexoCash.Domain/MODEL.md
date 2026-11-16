# Domain model

NexoCash tracks workdays, services, owned accounts, agency funds, cash, currency exchanges, and service consolidations.

- CUP, USD, MLC, and EUR are seeded when the database is created or upgraded. CUP starts as the primary currency. Currency codes remain fixed; active currencies can be renamed, removed from new operations, restored, or selected as primary.
- Default banknotes are 50, 100, 200, 500, 1000, 2000, and 5000 CUP; 1, 2, 5, 10, 20, 50, and 100 USD; and 5, 10, 20, 50, 100, 200, and 500 EUR. MLC has no physical banknotes. Denominations can be added, changed, or deactivated when no physical notes of that value remain.
- Reference exchange rates are dated directional values: one unit of the source currency equals the entered amount in the target currency. Actual currency exchanges use their own applied rate.

- Each account belongs to one owner and can hold balances in multiple currencies. Balances are calculated from movements, not stored on the account.
- Cash accounts are accounting allocations of a shared physical cash inventory. Virtual accounts do not participate in banknote counts.
- Agency funds are external balances calculated from agency fund movements. A service retains the commission rate and amount actually applied to it.
- Services have four recipient types: agency, fixed customer, one-off customer, and street pickup. Only the first two reference a saved contact. Each service records its date through a workday, scheduled time, optional origin and destination addresses, amount, currency, payment method (cash or transfer), and status (new, confirmed, or finished).
- Every service starts as New and advances to Confirmed, then Finished. A finished agency service writes its commission debit to the agency fund in the same transaction as the status change. New and confirmed services do not debit the fund.
- Service consolidation is the daily close. It includes only finished, unconsolidated services for the selected date; new and confirmed services remain pending. A later consolidation can include services finished afterward.
- Each consolidation writes the selected services and their account income movements in one database transaction. Allocation totals must match collected totals by currency and payment method. The optional banknote count covers all service cash across currencies and must match its total CUP value using saved reference rates.
- Account balances are calculated from movements. The general Movements view records deposits, withdrawals, and transfers. Manual cash deposits and withdrawals require banknote quantities; withdrawals check both the account balance and shared physical banknote inventory.
- A cash deposit can count banknotes in multiple currencies. Its expected amount and the counted currencies are compared in CUP, then the account receives separate income movements in the actual counted currencies. The denomination window reports currency subtotals, a CUP total, and a readable count summary.
- A transfer writes linked outgoing and incoming movements in one transaction. Moving between cash and virtual accounts records the physical banknotes leaving or entering; transfers between accounts of the same type leave the shared banknote inventory unchanged.
- Currency exchanges record outgoing and incoming movements and the actual rate received. Reference rates are stored separately.
- A cash count records banknote quantities. A reconciliation records the expected and counted amounts and adjustment movements per changed denomination. Physical inventory is calculated from recorded cash movements, consolidated service notes, and reconciliation adjustments.
- The account named Debt is an ordinary account used to reserve money for a later payment. It is not a liability account.
- Vehicle rent uses one configured account and starts tracking from the week in which that account is selected. The weekly view runs Monday through Sunday. Days are pending until marked worked or not worked; Sunday defaults to no work. Each worked day adds 9,000 CUP to that week's rent due.
- A movement involving the rent account records its chosen workday separately from the date when it was entered. This supports entering and consolidating older service days one at a time. Weekly available money comes from the account's actual CUP balance, including extra money carried from earlier weeks; movement amounts are not split across days.
- Rent payments are expense movements from the configured account and may be made in parts through a selected date. They cannot exceed rent due for worked days or the account balance. Physical payments record exact banknotes; the counter can suggest available notes. Older unpaid rent is shown before newer weeks can be paid.
- Monetary values use decimal in C# and exact decimal text in SQLite; SQLite REAL is not used for money.

The desktop application includes an overview, workday marking, agency and customer management, service consolidation, owned accounts, a general movement history, shared cash inventory and reconciliation, actual currency exchanges, and settings for owners, currencies, rates, and denominations.
