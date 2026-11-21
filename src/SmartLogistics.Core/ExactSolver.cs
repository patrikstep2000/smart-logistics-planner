using System.Diagnostics;
using System.Numerics;

namespace SmartLogistics.Core;

/// <summary>
/// Egzaktni solver: dinamičko programiranje po kamionu + optimalna podjela naloga.
///
/// KORAK 1 – DP za jedan kamion (<see cref="TruckDp"/>)
///   Stanje = (status svakog naloga, trenutna lokacija kamiona).
///   Status naloga je 0 = čeka, 1 = u kamionu, 2 = isporučen.
///   Svi statusi zajedno kodiraju se kao broj u bazi 3 (npr. 3 naloga → 0..26).
///   Prijelaz = jedna radnja: preuzmi nalog koji čeka (samo ako stane po kg i broju
///   strojeva) ili isporuči nalog koji je u kamionu. Svaki prijelaz povećava kod
///   stanja, pa se najkraći put računa jednim prolazom od manjih prema većim kodovima.
///
///   Stanja u kojima je kamion prazan su moguće završne točke: dodamo povratak u bazu
///   i dobijemo najkraću rutu koja izvršava točno taj skup naloga. Jedan prolaz tako
///   daje optimalnu kilometražu za SVAKI podskup naloga.
///
///   Radno vrijeme: za zadani podskup broj radnji je fiksan (2 po nalogu), pa vrijeme
///   raste zajedno s kilometražom. Ako najkraća ruta ne stane u smjenu, ne stane ni
///   jedna – zato je provjera na kraju dovoljna i egzaktna.
///
/// KORAK 2 – podjela naloga između kamiona
///   Za svaku podjelu zbrojimo optimalne kilometraže kamiona. Cilj je:
///   najprije što više izvršenih naloga, zatim najmanja ukupna kilometraža.
///   Rezultat je dokazani optimum.
/// </summary>
public static class ExactSolver
{
    /// <summary>3^12 stanja je ispod sekunde; iznad toga memorija i vrijeme rastu eksponencijalno.</summary>
    public const int MaxOrdersPerTruck = 12;

    public static Plan Solve(Problem problem)
    {
        var errors = problem.InputErrors();
        if (errors.Count > 0)
            throw new InvalidDataException("Neispravni ulazni podaci:\n" + string.Join("\n", errors));
        if (problem.Orders.Count > 62)
            throw new InvalidOperationException("Egzaktni solver podržava najviše 62 naloga ukupno.");

        var sw = Stopwatch.StartNew();
        var orders = problem.Orders;

        // KORAK 1: DP za svaki kamion, samo s nalozima čiji stroj taj kamion uopće može nositi.
        var dps = new List<TruckDp>();
        foreach (var truck in problem.Trucks)
        {
            var carriable = Enumerable.Range(0, orders.Count)
                .Where(i => problem.Weight(orders[i]) <= truck.MaxKg)
                .ToList();
            if (carriable.Count > MaxOrdersPerTruck)
                throw new InvalidOperationException(
                    $"{truck.Name} može prevesti {carriable.Count} naloga; egzaktni solver podržava najviše " +
                    $"{MaxOrdersPerTruck} po kamionu. Za veće probleme potreban je heuristički solver (OR-Tools).");
            dps.Add(new TruckDp(problem, truck, carriable));
        }

        // KORAK 2: kombiniranje kamiona. Maska = skup izvršenih naloga (bit i = nalog i).
        // best[maska] = (ukupni km u dk, maska naloga za svaki dosad obrađeni kamion)
        var best = new Dictionary<long, (int Cost, long[] PerTruck)> { [0] = (0, []) };
        foreach (var dp in dps)
        {
            var next = new Dictionary<long, (int Cost, long[] PerTruck)>();
            foreach (var (mask, (cost, perTruck)) in best)
            {
                foreach (var (truckMask, (truckCost, _)) in dp.Best)
                {
                    if ((mask & truckMask) != 0) continue;   // nalog ne smiju voziti dva kamiona
                    long newMask = mask | truckMask;
                    int newCost = cost + truckCost;
                    if (!next.TryGetValue(newMask, out var current) || newCost < current.Cost)
                        next[newMask] = (newCost, [.. perTruck, truckMask]);
                }
            }
            best = next;
        }

        // Najviše naloga, zatim najmanje km.
        long bestMask = best.Keys
            .OrderByDescending(m => BitOperations.PopCount((ulong)m))
            .ThenBy(m => best[m].Cost)
            .ThenBy(m => m)
            .First();
        var chosen = best[bestMask].PerTruck;

        // Rute se ponovno "voze" kroz validator – on računa korake, teret i vrijeme za prikaz.
        var routes = dps.Select((dp, t) => Validator.SimulateRoute(problem, dp.Truck, dp.StopsFor(chosen[t]))).ToList();

        string timeReason =
            $"Nalog se ne može uklopiti u radno vrijeme vozača ({problem.Settings.MaxShiftMin / 60:0.##} h) uz ostala ograničenja.";
        var unassigned = Enumerable.Range(0, orders.Count)
            .Where(i => (bestMask >> i & 1) == 0)
            .Select(i => Unassigned.For(problem, orders[i], timeReason))
            .ToList();

        return new Plan
        {
            Routes = routes,
            Unassigned = unassigned,
            SolverName = "Egzaktni DP",
            IsOptimal = true,
            ElapsedMs = sw.ElapsedMilliseconds,
            StatesExplored = dps.Sum(d => d.StatesExplored),
        };
    }

    /// <summary>Optimalne rute jednog kamiona za sve podskupove naloga koje može prevesti.</summary>
    private sealed class TruckDp
    {
        private const int Inf = int.MaxValue;

        public Truck Truck { get; }
        public int StatesExplored { get; private set; }

        /// <summary>Globalna maska isporučenih naloga → (km u dk, indeks završnog stanja).</summary>
        public Dictionary<long, (int Cost, int State)> Best { get; } = new();

        private readonly List<Order> _orders;
        private readonly int[] _parent;
        private readonly int[] _action;   // 2k = preuzmi nalog k, 2k+1 = isporuči nalog k, -1 = početak

        public TruckDp(Problem p, Truck truck, List<int> globalIndices)
        {
            Truck = truck;
            _orders = globalIndices.Select(i => p.Orders[i]).ToList();
            int n = _orders.Count;

            // Lokacije relevantne za ovaj kamion; indeks 0 je baza.
            var locIds = new List<string> { p.Base };
            foreach (var o in _orders)
                foreach (var l in new[] { o.Pickup, o.Delivery })
                    if (!locIds.Contains(l)) locIds.Add(l);
            int L = locIds.Count;
            var dist = new int[L, L];
            for (int a = 0; a < L; a++)
                for (int b = 0; b < L; b++)
                    dist[a, b] = Distance.LegDk(p.Locations[locIds[a]], p.Locations[locIds[b]]);

            var pow3 = new int[n];
            int size = 1;
            for (int k = 0; k < n; k++) { pow3[k] = size; size *= 3; }

            var weights = _orders.Select(p.Weight).ToArray();
            var pickLoc = _orders.Select(o => locIds.IndexOf(o.Pickup)).ToArray();
            var delLoc = _orders.Select(o => locIds.IndexOf(o.Delivery)).ToArray();

            // cost[status * L + lokacija] = najmanji km (dk) do tog stanja
            var cost = new int[size * L];
            Array.Fill(cost, Inf);
            _parent = new int[size * L];
            _action = new int[size * L];
            Array.Fill(_action, -1);
            cost[0] = 0;   // svi nalozi čekaju, kamion je u bazi

            var digits = new int[n];
            var moves = new List<(int NextRow, int Target, int Action)>(n);

            for (int s = 0; s < size; s++)
            {
                int row = s * L;
                bool reachable = false;
                for (int l = 0; l < L && !reachable; l++) reachable = cost[row + l] != Inf;
                if (!reachable) continue;   // npr. stanje koje bi prekoračilo nosivost
                StatesExplored++;

                // Dekodiraj status svakog naloga iz broja u bazi 3.
                int x = s, onboardKg = 0, onboardN = 0, doneN = 0;
                for (int k = 0; k < n; k++)
                {
                    digits[k] = x % 3;
                    x /= 3;
                    if (digits[k] == 1) { onboardKg += weights[k]; onboardN++; }
                    else if (digits[k] == 2) doneN++;
                }

                // Prazan kamion → moguć kraj rute: povratak u bazu.
                if (onboardN == 0)
                {
                    int endCost = Inf, endState = -1;
                    for (int l = 0; l < L; l++)
                    {
                        int c = cost[row + l];
                        if (c != Inf && c + dist[l, 0] < endCost) { endCost = c + dist[l, 0]; endState = row + l; }
                    }
                    if (endState >= 0 && Distance.FitsShift(endCost, 2 * doneN, p.Settings))
                    {
                        long mask = 0;
                        for (int k = 0; k < n; k++)
                            if (digits[k] == 2) mask |= 1L << globalIndices[k];
                        Best[mask] = (endCost, endState);
                    }
                }

                // Dopuštene radnje iz ovog statusa (ne ovise o lokaciji).
                moves.Clear();
                for (int k = 0; k < n; k++)
                {
                    if (digits[k] == 0 && onboardKg + weights[k] <= truck.MaxKg && onboardN + 1 <= truck.MaxItems)
                        moves.Add(((s + pow3[k]) * L, pickLoc[k], 2 * k));
                    else if (digits[k] == 1)
                        moves.Add(((s + pow3[k]) * L, delLoc[k], 2 * k + 1));
                }

                // Opusti sve prijelaze (klasični korak najkraćeg puta).
                for (int l = 0; l < L; l++)
                {
                    int c = cost[row + l];
                    if (c == Inf) continue;
                    foreach (var (nextRow, target, action) in moves)
                    {
                        int nc = c + dist[l, target];
                        int ni = nextRow + target;
                        if (nc < cost[ni])
                        {
                            cost[ni] = nc;
                            _parent[ni] = row + l;
                            _action[ni] = action;
                        }
                    }
                }
            }
        }

        /// <summary>Rekonstrukcija rute: od završnog stanja unatrag preko roditelja.</summary>
        public List<Stop> StopsFor(long mask)
        {
            int state = Best[mask].State;
            var acts = new List<int>();
            while (_action[state] != -1)
            {
                acts.Add(_action[state]);
                state = _parent[state];
            }
            acts.Reverse();
            return acts.Select(a => new Stop(_orders[a / 2].Id, a % 2 == 0 ? StepAction.Pickup : StepAction.Delivery))
                .ToList();
        }
    }
}
