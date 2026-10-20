using System.Numerics;

namespace Caero;

// Every yield bounds a unit of arithmetic. Passing probable-prime tests is never
// enough to turn a sign red: larger primes also receive an exact n-1 proof.
internal sealed class IncrementalPrime : IDisposable
{
    private static readonly int[] SmallPrimes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53];
    private readonly IEnumerator<int> work;
    public bool? Result { get; private set; }
    internal long Steps { get; private set; }
    public IncrementalPrime(BigInteger number) => work = Prove(number, value => Result = value).GetEnumerator();
    public void Step()
    {
        if (Result.HasValue) return;
        Steps++;
        if (!work.MoveNext() && !Result.HasValue) throw new InvalidOperationException("Incomplete primality proof.");
    }
    public void Dispose() => work.Dispose();

    private static IEnumerable<int> Prove(BigInteger n, Action<bool> finish)
    {
        if (n < 2) { finish(false); yield break; }
        foreach (int p in SmallPrimes)
        {
            if (n == p) { finish(true); yield break; }
            if (n % p == 0) { finish(false); yield break; }
            if ((BigInteger)p * p > n) { finish(true); yield break; }
            yield return 0;
        }
        // Cheap exact base cases terminate recursive factor certificates.
        if (n < 10_000)
        {
            for (int d = 55; (BigInteger)d * d <= n; d += 2)
            {
                if (n % d == 0) { finish(false); yield break; }
                yield return 0;
            }
            finish(true); yield break;
        }
        BigInteger odd = n - 1; int shifts = 0;
        while (odd.IsEven) { odd >>= 1; shifts++; }
        // Miller-Rabin supplies composite witnesses only; no probabilistic acceptance.
        foreach (int a in SmallPrimes)
        {
            BigInteger x = BigInteger.ModPow(a, odd, n);
            yield return 0;
            if (x == 1 || x == n - 1) continue;
            bool passed = false;
            for (int r = 1; r < shifts; r++)
            {
                x = x * x % n;
                yield return 0;
                if (x == n - 1) { passed = true; break; }
            }
            if (!passed) { finish(false); yield break; }
        }
        var factors = new List<BigInteger>();
        foreach (int step in Factor(n - 1, factors)) yield return step;
        // Complete factorization of n-1 into proved primes. For every factor q,
        // the gcd condition forces each prime divisor of n to be 1 modulo q^e.
        // Thus every such divisor is >= n, proving n itself prime.
        foreach (BigInteger q in factors.Distinct())
        {
            for (BigInteger a = 2; ; a++)
            {
                BigInteger fermat = BigInteger.ModPow(a, n - 1, n);
                yield return 0;
                if (fermat != 1) { finish(false); yield break; }
                BigInteger power = BigInteger.ModPow(a, (n - 1) / q, n);
                yield return 0;
                BigInteger gcd = BigInteger.GreatestCommonDivisor(power - 1, n);
                yield return 0;
                if (gcd == 1) break;
                if (gcd != n) { finish(false); yield break; }
            }
        }
        finish(true);
    }

    private static IEnumerable<int> Factor(BigInteger n, List<BigInteger> result)
    {
        foreach (int p in SmallPrimes)
        {
            while (n % p == 0)
            {
                result.Add(p); n /= p;
                yield return 0;
            }
        }
        if (n == 1) yield break;
        bool prime = false;
        foreach (int step in Prove(n, value => prime = value)) yield return step;
        if (prime) { result.Add(n); yield break; }
        BigInteger divisor = n;
        // Deterministic Pollard rho, suspended after each small iteration.
        for (BigInteger c = 1; divisor == n; c++)
        {
            BigInteger x = 2, y = 2; divisor = 1;
            while (divisor == 1)
            {
                x = (x * x + c) % n;
                y = (y * y + c) % n; y = (y * y + c) % n;
                divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), n);
                yield return 0;
            }
        }
        foreach (int step in Factor(divisor, result)) yield return step;
        foreach (int step in Factor(n / divisor, result)) yield return step;
    }
}
