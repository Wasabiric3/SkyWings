using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Demo data used by the reports screens (ports seedMockCompletedBookings / seedMockCancelledBookings).
/// Uses JavaRandom(42) so the generated rows are the same as in the Java version.
/// </summary>
public sealed partial class BookingStore
{
    private static readonly string[] SeedNames =
    {
        "Maria Santos", "Juan dela Cruz", "Ana Reyes", "Pedro Ramos",
        "Rosa Garcia", "Carlos Mendoza", "Elena Villanueva", "Miguel Torres",
        "Luz Fernandez", "Roberto Aquino", "Celine Bautista", "Jose Pascual",
        "Grace Morales", "Emmanuel Cruz", "Patricia Lim", "Antonio Gomez",
        "Marisol Castillo", "Danilo Navarro", "Teresita Ocampo", "Rafael Dizon"
    };

    private static readonly string[] SeedAddresses =
    {
        "123 Rizal St, Makati City", "456 Mabini Ave, Quezon City", "789 Luna Blvd, Cebu City",
        "321 Bonifacio St, Davao City", "654 Del Pilar St, Manila"
    };


    private const string InsBook =
        "INSERT OR IGNORE INTO bookings " +
        "(reference,booked_at,flight_no,origin,destination,flight_date," +
        "domestic,class_name,payment_method,masked_account,total_amount," +
        "realtime_est,status,payment_status,payment_txn_ref," +
        "payment_confirmed_at,refund_amount) " +
        "VALUES (@ref,@booked,@fn,@org,@dst,@fdate,@dom,@cls,@pay,@acct,@total,@est,@status,@paySt,@txn,@payAt,@refund)";

    private const string InsPax =
        "INSERT INTO passengers " +
        "(booking_ref,name,address,contact,age,birthday,id_number," +
        "senior,pwd,seat_code,addons,total_amount) " +
        "VALUES (@ref,@name,@addr,@contact,@age,@bday,@id,0,0,@seat,'',@total)";

    private static void InsertBooking(SqliteConnection con, SqliteTransaction tx, string reference, DateTime bookedAt,
        string fn, string org, string dst, DateTime flDate, int dom, string cls, string pay, string acct,
        decimal total, string est, string status, string payStatus, string txn, DateTime payAt, decimal refund)
    {
        using var cmd = Cmd(con, InsBook, tx,
            ("@ref", reference), ("@booked", bookedAt), ("@fn", fn), ("@org", org), ("@dst", dst),
            ("@fdate", flDate.Date), ("@dom", dom), ("@cls", cls), ("@pay", pay), ("@acct", acct),
            ("@total", total), ("@est", est), ("@status", status), ("@paySt", payStatus),
            ("@txn", txn), ("@payAt", payAt), ("@refund", refund));
        cmd.ExecuteNonQuery();
    }

    private static void InsertPassenger(SqliteConnection con, SqliteTransaction tx, string reference, string name,
        string addr, string contact, int age, string bday, string id, string seat, decimal total)
    {
        using var cmd = Cmd(con, InsPax, tx,
            ("@ref", reference), ("@name", name), ("@addr", addr), ("@contact", contact), ("@age", age),
            ("@bday", bday), ("@id", id), ("@seat", seat), ("@total", total));
        cmd.ExecuteNonQuery();
    }

    private static int SeatColumns(string className)
        => className == "Economy" ? 6 : className == "Premium Economy" ? 4 : 3;

    public void SeedMockCompletedBookings()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using (var chk = Cmd(con, "SELECT COUNT(*) FROM bookings WHERE reference LIKE 'M%'"))
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return;

                var flightData = new (string No, string Org, string Dst, decimal Base, int Dom)[]
                {
                    ("PR-101", "Manila (MNL)", "Cebu (CEB)", 3500.0m, 1),
                    ("PR-102", "Manila (MNL)", "Davao (DVO)", 4200.0m, 1),
                    ("PR-103", "Manila (MNL)", "Boracay (MPH)", 3800.0m, 1),
                    ("PR-201", "Manila (MNL)", "Singapore (SIN)", 8500.0m, 0),
                    ("PR-202", "Manila (MNL)", "Bangkok (BKK)", 7800.0m, 0),
                };
                string[] dates = { "2026-05-12", "2026-05-13", "2026-05-14", "2026-05-15", "2026-05-16", "2026-05-17" };
                var classDist = new (string Name, decimal Mult)[]
                {
                    ("Economy", 1.0m), ("Economy", 1.0m), ("Economy", 1.0m), ("Economy", 1.0m),
                    ("Premium Economy", 1.8m), ("Business", 3.0m), ("First Class", 5.0m)
                };
                string[] payMethods = { "Credit Card", "E-Wallet", "Debit Card" };
                string[] maskedAccts = { "**** 4521", "**** 7832", "**** 3310", "**** 9087", "**** 6641" };

                int refNum = 1;
                var rnd = new JavaRandom(42);
                var inv = CultureInfo.InvariantCulture;

                using var tx = con.BeginTransaction();
                foreach (var fl in flightData)
                {
                    int seatRow = 1;
                    foreach (var dateStr in dates)
                    {
                        DateTime flDate = DateTime.ParseExact(dateStr, "yyyy-MM-dd", inv);
                        int bPerDay = 6 + rnd.NextInt(5);

                        for (int b = 0; b < bPerDay; b++)
                        {
                            string reference = "M" + (refNum++).ToString("D4", inv);
                            var cls = classDist[b % classDist.Length];
                            string seatPfx = AppData.SeatPrefixForClass(cls.Name);
                            int pax = 1 + rnd.NextInt(3);
                            decimal totalAmt = AppData.Round2(fl.Base * cls.Mult * pax);
                            DateTime bookedDate = flDate.AddDays(-(2 + rnd.NextInt(12)));
                            int hh = 9 + rnd.NextInt(10);
                            int mm = rnd.NextInt(60);
                            DateTime bookedAt = bookedDate.AddHours(hh).AddMinutes(mm);
                            DateTime dep = flDate.AddHours(6 + (b % 4) * 2);
                            DateTime arr = dep.AddMinutes(fl.Dom == 1 ? 90 : 240);
                            string estStr = dep.ToString(AppData.EstDateTimeFormat, inv) + " -> " + arr.ToString(AppData.EstDateTimeFormat, inv);
                            string txnRef = "TXN-" + rnd.NextInt(100000000).ToString("D8", inv);

                            InsertBooking(con, tx, reference, bookedAt, fl.No, fl.Org, fl.Dst, flDate, fl.Dom,
                                cls.Name, payMethods[b % payMethods.Length], maskedAccts[b % maskedAccts.Length],
                                totalAmt, estStr, AppData.StatusConfirmed, AppData.PaymentPaid, txnRef, bookedAt.AddMinutes(5), 0m);

                            for (int p = 0; p < pax; p++)
                            {
                                string pName = SeedNames[(refNum + p) % SeedNames.Length];
                                string pAddr = SeedAddresses[p % SeedAddresses.Length];
                                string contact = "09" + (100000000 + rnd.NextInt(900000000)).ToString("D9", inv);
                                int age = 22 + rnd.NextInt(36);
                                int bMonth = 1 + rnd.NextInt(12);
                                int bDay = 1 + rnd.NextInt(28);
                                string bday = (2026 - age) + "-" + bMonth.ToString("D2", inv) + "-" + bDay.ToString("D2", inv);
                                string idNum = "PSP-" + rnd.NextInt(10000000).ToString("D7", inv);
                                char col = (char)('A' + (p % SeatColumns(cls.Name)));
                                string seat = seatPfx + "-" + seatRow.ToString("D2", inv) + col;
                                InsertPassenger(con, tx, reference, pName, pAddr, contact, age, bday, idNum, seat, AppData.Round2(totalAmt / pax));
                            }
                            seatRow++;
                        }
                    }
                }

                InsertCancelledSeed(con, tx);   // the Java method also inserted the 17 cancelled rows here
                tx.Commit();
            }
            catch (Exception e)
            {
                AppLogger.Error("BookingStore.SeedCompleted", e);
            }
        }
    }

    public void SeedMockCancelledBookings()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using (var chk = Cmd(con, "SELECT COUNT(*) FROM bookings WHERE reference = 'M0301'"))
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return;

                using var tx = con.BeginTransaction();
                InsertCancelledSeed(con, tx);
                tx.Commit();
            }
            catch (Exception e)
            {
                AppLogger.Error("BookingStore.SeedCancelled", e);
            }
        }
    }

    private static void InsertCancelledSeed(SqliteConnection con, SqliteTransaction tx)
    {
        var inv = CultureInfo.InvariantCulture;
        // ref, flightNo, origin, dest, date, domestic, class, multiplier, pax, payMethod, maskedAcct, refundRatio
        var data = new (string Ref, string Fn, string Org, string Dst, string Date, int Dom, string Cls,
            decimal Mult, int Pax, string Pay, string Acct, decimal Ratio)[]
        {
            ("M0301", "PR-101", "Manila (MNL)", "Cebu (CEB)", "2026-05-12", 1, "Economy", 1.0m, 1, "Credit Card", "**** 4521", 1.0m),
            ("M0302", "PR-102", "Manila (MNL)", "Davao (DVO)", "2026-05-13", 1, "Economy", 1.0m, 2, "E-Wallet", "**** 7832", 0.2m),
            ("M0303", "PR-103", "Manila (MNL)", "Boracay (MPH)", "2026-05-14", 1, "Premium Economy", 1.8m, 1, "Debit Card", "**** 3310", 0.5m),
            ("M0304", "PR-201", "Manila (MNL)", "Singapore (SIN)", "2026-05-12", 0, "Economy", 1.0m, 3, "Credit Card", "**** 9087", 1.0m),
            ("M0305", "PR-202", "Manila (MNL)", "Bangkok (BKK)", "2026-05-13", 0, "Business", 3.0m, 1, "E-Wallet", "**** 6641", 0.5m),
            ("M0306", "PR-101", "Manila (MNL)", "Cebu (CEB)", "2026-05-14", 1, "Economy", 1.0m, 2, "Debit Card", "**** 4521", 0.2m),
            ("M0307", "PR-102", "Manila (MNL)", "Davao (DVO)", "2026-05-15", 1, "Premium Economy", 1.8m, 1, "Credit Card", "**** 7832", 1.0m),
            ("M0308", "PR-201", "Manila (MNL)", "Singapore (SIN)", "2026-05-16", 0, "Economy", 1.0m, 2, "E-Wallet", "**** 3310", 0.2m),
            ("M0309", "PR-202", "Manila (MNL)", "Bangkok (BKK)", "2026-05-17", 0, "Economy", 1.0m, 1, "Credit Card", "**** 9087", 0.5m),
            ("M0310", "PR-103", "Manila (MNL)", "Boracay (MPH)", "2026-05-15", 1, "Business", 3.0m, 1, "Debit Card", "**** 6641", 1.0m),
            ("M0311", "PR-101", "Manila (MNL)", "Cebu (CEB)", "2026-05-16", 1, "First Class", 5.0m, 1, "Credit Card", "**** 4521", 0.2m),
            ("M0312", "PR-202", "Manila (MNL)", "Bangkok (BKK)", "2026-05-14", 0, "Economy", 1.0m, 3, "E-Wallet", "**** 7832", 1.0m),
            ("M0313", "PR-102", "Manila (MNL)", "Davao (DVO)", "2026-05-12", 1, "Economy", 1.0m, 2, "Debit Card", "**** 3310", 0.2m),
            ("M0314", "PR-201", "Manila (MNL)", "Singapore (SIN)", "2026-05-17", 0, "Premium Economy", 1.8m, 1, "Credit Card", "**** 9087", 0.5m),
            ("M0315", "PR-103", "Manila (MNL)", "Boracay (MPH)", "2026-05-13", 1, "Economy", 1.0m, 1, "E-Wallet", "**** 6641", 0.2m),
            ("M0316", "PR-101", "Manila (MNL)", "Cebu (CEB)", "2026-05-17", 1, "Business", 3.0m, 2, "Debit Card", "**** 4521", 1.0m),
            ("M0317", "PR-202", "Manila (MNL)", "Bangkok (BKK)", "2026-05-15", 0, "Economy", 1.0m, 2, "Credit Card", "**** 7832", 0.5m),
        };

        int cIdx = 0;
        foreach (var cx in data)
        {
            decimal cBase = cx.Fn == "PR-101" ? 3500.0m
                : cx.Fn == "PR-102" ? 4200.0m
                : cx.Fn == "PR-103" ? 3800.0m
                : cx.Fn == "PR-201" ? 8500.0m : 7800.0m;
            decimal cTotal = AppData.Round2(cBase * cx.Mult * cx.Pax);
            decimal cRefund = AppData.Round2(cTotal * cx.Ratio);
            string cPaySt = cx.Ratio > 0 ? AppData.PaymentRefunded : AppData.PaymentPaid;
            DateTime cFlDate = DateTime.ParseExact(cx.Date, "yyyy-MM-dd", inv);
            DateTime cBookedAt = cFlDate.AddDays(-5).AddHours(10).AddMinutes(30);
            DateTime cDep = cFlDate.AddHours(8);
            DateTime cArr = cDep.AddMinutes(cx.Dom == 1 ? 90 : 240);
            string cEst = cDep.ToString(AppData.EstDateTimeFormat, inv) + " -> " + cArr.ToString(AppData.EstDateTimeFormat, inv);
            string cTxn = "TXN-CNCL" + (cIdx + 1).ToString("D4", inv);

            InsertBooking(con, tx, cx.Ref, cBookedAt, cx.Fn, cx.Org, cx.Dst, cFlDate, cx.Dom, cx.Cls, cx.Pay,
                cx.Acct, cTotal, cEst, "CANCELED", cPaySt, cTxn, cBookedAt.AddMinutes(5), cRefund);

            for (int p = 0; p < cx.Pax; p++)
            {
                string pName = SeedNames[(cIdx * 3 + p) % SeedNames.Length];
                string pAddr = SeedAddresses[p % SeedAddresses.Length];
                string contact = "09" + (900000000L + cIdx * 10000L + p).ToString("D9", inv);
                int age = 22 + (cIdx + p) % 36;
                string bday = (2026 - age) + "-" + (1 + p % 12).ToString("D2", inv) + "-" + (1 + cIdx % 28).ToString("D2", inv);
                string idNum = "PSP-CX" + (cIdx * 10 + p).ToString("D5", inv);
                char col = (char)('A' + (p % SeatColumns(cx.Cls)));
                string seat = AppData.SeatPrefixForClass(cx.Cls) + "-" + (cIdx + 1).ToString("D2", inv) + col;
                InsertPassenger(con, tx, cx.Ref, pName, pAddr, contact, age, bday, idNum, seat, AppData.Round2(cTotal / cx.Pax));
            }
            cIdx++;
        }
    }
}
