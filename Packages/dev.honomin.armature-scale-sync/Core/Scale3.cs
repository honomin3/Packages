// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Globalization;

namespace Honomin.ArmatureScaleSync.Core
{
    /// <summary>A local multiplier. World scale ratios and TRS decompositions are deliberately absent.</summary>
    public readonly struct Scale3 : IEquatable<Scale3>
    {
        public readonly double X, Y, Z;
        public Scale3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public static Scale3 One => new Scale3(1, 1, 1);
        public bool IsUniform => X == Y && Y == Z;
        public bool IsNegative => X < 0 || Y < 0 || Z < 0;
        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        public bool IsInvertible => X != 0 && Y != 0 && Z != 0 && IsFinite;
        public bool IsFloatRepresentable => Representable(X) && Representable(Y) && Representable(Z);
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static bool Representable(double x) => Finite(x) && Math.Abs(x) <= float.MaxValue && Math.Abs(x) >= float.Epsilon;
        public static Scale3 operator *(Scale3 a, Scale3 b) => new Scale3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public bool Equals(Scale3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Scale3 s && Equals(s);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", X, Y, Z);
    }
}
