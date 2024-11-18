using System.Globalization;

namespace Biohazrd.Expressions
{
    public sealed record DoubleConstant : ConstantValue
    {
        public double Value { get; init; }

        public DoubleConstant(double value)
            => Value = value;

        public override string ToString()
            => Value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
