using System;
using System.Threading;

// Copyright (c) 2026 Gavin W. Landon (chizl.com)
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// SPDX-License-Identifier: MIT
namespace Chizl.ThreadSupport
{
    /// <summary>
    /// Represents a thread-safe, atomically updatable date and time value with DateTimeKind awareness.<br/>
    /// Default: DateTimeKind with Local if not specified in DateTime object passed in.<br/>
    /// </summary>
    internal sealed class ADateTime : IEquatable<ADateTime>
    {
        // Use a long to store Ticks
        // for atomic updates and reads
        private long _ticks;
        // Faster than calling DateTime.Kind Enum on each read. Set once at construction
        // and never changes, so no need for Interlocked here.  Default local, even if Kind
        // is unspecified, to match DateTime.Now behavior.
        private ABool _isUTC = ABool.False;
        private DateTime _dateTime;

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the ADateTime class with the current local time.
        /// </summary>
        public ADateTime()
        {
            // Store the Ticks value, that will be used atomically later. UTC flag is false by default to match DateTime.Now behavior.
            _ticks = DateTime.Now.Ticks;
            // Initialize the internal DateTime based on the initial value, though it will be updated atomically later, it's not
            // needed in the constructor.  By updating this variable, this use of properties for Value faster and not have to
            // recreate DateTime for each property.
            _dateTime = new DateTime(_ticks, (this.IsUTC ? DateTimeKind.Utc : DateTimeKind.Local));
        }

        /// <summary>
        /// Initializes a new instance of the ADateTime structure using the specified DateTime value.
        /// </summary>
        /// <param name="value">The DateTime value to initialize with.</param>
        public ADateTime(DateTime value)
        {
            // Set the UTC flag based on the DateTimeKind of the input value
            _isUTC.SetVal(value.Kind == DateTimeKind.Utc);
            // Store the Ticks value, that will be used atomically later.
            _ticks = value.Ticks;
            // Initialize the internal DateTime based on the initial value, though it will be updated atomically later, it's not
            // needed in the constructor.  By updating this variable, this use of properties for Value faster and not have to
            // recreate DateTime for each property.
            _dateTime = new DateTime(_ticks, (this.IsUTC ? DateTimeKind.Utc : DateTimeKind.Local));
        }
        #endregion

        #region Public Static Properties
        /// <summary>
        /// Gets the current date and time on the local computer.
        /// </summary>
        public static ADateTime Now => new ADateTime(DateTime.Now);
        /// <summary>
        /// Gets the current date and time in Coordinated Universal Time (UTC).
        /// </summary>
        public static ADateTime UtcNow => new ADateTime(DateTime.UtcNow);
        /// <summary>
        /// Gets the minimum representable date and time.
        /// </summary>
        public static ADateTime MinValue => new ADateTime(DateTime.MinValue);
        /// <summary>
        /// Gets the maximum representable date and time.
        /// </summary>
        public static ADateTime MaxValue => new ADateTime(DateTime.MaxValue);
        #endregion

        #region Public Properties
        /// <summary>
        /// Indicates whether the time is in Coordinated Universal Time (UTC).
        /// </summary>
        public bool IsUTC { get { return _isUTC; } }
        /// <summary>
        /// Gets the date component of the value, with the time component set to 00:00:00.
        /// </summary>
        public DateTime Date => this.Value.Date;
        /// <summary>
        /// Gets the time of day component of the date represented by this instance.
        /// </summary>
        public TimeSpan TimeOfDay => this.Value.TimeOfDay;
        /// <summary>
        /// Gets the day of the year represented by the current value.
        /// </summary>
        public int DayOfYear => this.Value.DayOfYear;
        /// <summary>
        /// Gets the year component of the date represented by this instance.
        /// </summary>
        public int Year => this.Value.Year;
        /// <summary>
        /// Gets the month component of the date represented by this instance.
        /// </summary>
        public int Month => this.Value.Month;
        /// <summary>
        /// Gets the day component of the date represented by this instance.
        /// </summary>
        public int Day => this.Value.Day;
        /// <summary>
        /// Gets the day of the week represented by the current value.
        /// </summary>
        public DayOfWeek DayOfWeek => this.Value.DayOfWeek;
        /// <summary>
        /// Gets the kind of the date represented by this instance.
        /// </summary>
        public DateTimeKind Kind => this.Value.Kind;
        /// <summary>
        /// Gets the hour component of the date represented by this instance.
        /// </summary>
        public int Hour => this.Value.Hour;
        /// <summary>
        /// Gets the minute component of the date represented by this instance.
        /// </summary>
        public int Minute => this.Value.Minute;
        /// <summary>
        /// Gets the second component of the date represented by this instance.
        /// </summary>
        public int Second => this.Value.Second;
        /// <summary>
        /// Gets the millisecond component of the date represented by this instance.
        /// </summary>
        public int Millisecond => this.Value.Millisecond;
        /// <summary>
        /// Gets the number of ticks that represent the date and time of this instance.
        /// </summary>
        public long Ticks => this.Value.Ticks;
#if NET5_0_OR_GREATER
        /// <summary>
        /// Gets the microsecond component of the date represented by this instance.
        /// </summary>
        public int Microsecond => this.Value.Microsecond;
        /// <summary>
        /// Gets the nanosecond component of the date represented by this instance.
        /// </summary>
        public int Nanosecond => this.Value.Nanosecond;
#endif
        /// <summary>
        /// Gets the current value as a DateTime, read atomically.
        /// </summary>
        public DateTime Value => _dateTime;     // Already calculated new DateTime(Interlocked.Read(ref _ticks), (this.IsUTC ? DateTimeKind.Utc : DateTimeKind.Local));
        #endregion

        #region Public Methods
        /// <summary>
        /// Updates the internal time value to match the specified <see cref="DateTime"/> instance.<br/>
        /// Kind will be optionally updated based on the DateTime argument.  If arg is true and unspecified, 
        /// it will be treated as local to match DateTime.Now behavior.  This method is thread-safe 
        /// and can be called concurrently from multiple threads without risking data corruption or 
        /// inconsistent state.  The internal time value is updated atomically to ensure that all 
        /// threads see a consistent and up-to-date value after the adjustment.
        /// </summary>
        /// <param name="dt">The <see cref="DateTime"/> value to set.</param>
        /// <param name="updateKind">(Default: false), true to update the UTC flag based on the DateTimeKind of dt; otherwise, false.</param>
        /// <returns>The current <see cref="ADateTime"/> instance with the updated time.</returns>
        public ADateTime AdjustTime(DateTime dt, bool updateKind = false)
        {
            if (updateKind)
            {
                // Set the UTC flag based on the DateTimeKind of the input value
                if (dt.Kind == DateTimeKind.Utc)
                    _isUTC.SetTrue();
                else
                    _isUTC.SetFalse();
            }

            SafeExchange(dt.Ticks);
            return this;
        }
        /// <summary>
        /// Adjusts the current date and time by the specified time interval, ahead or 
        /// behind, while handling potential overflow scenarios gracefully.<br/>
        /// Kind is not updated by this method, as it is an adjustment to the existing 
        /// time value rather than a replacement.<br/>
        /// If the adjustment would cause the internal time to exceed the maximum 
        /// or minimum representable DateTime values, it caps the value at DateTime.MaxValue 
        /// or DateTime.MinValue accordingly, ensuring that the ADateTime instance remains 
        /// in a valid state without throwing an exception due to overflow.  This method 
        /// is thread-safe and can be called concurrently from multiple threads without 
        /// risking data corruption or inconsistent state.
        /// </summary>
        /// <param name="ts">The time interval to add.</param>
        /// <returns>The updated ADateTime instance.</returns>
        public ADateTime AdjustTime(TimeSpan ts)
        {
            long newTicks = 0;
            try
            {
                checked
                {
                    newTicks = Interlocked.Read(ref _ticks) + ts.Ticks;
                }

                if (newTicks > DateTime.MaxValue.Ticks)
                    newTicks = DateTime.MaxValue.Ticks;
                else if (newTicks < DateTime.MinValue.Ticks)
                    newTicks = DateTime.MinValue.Ticks;

                SafeExchange(newTicks);
            }
            catch (OverflowException)
            {
                checked
                {
                    // Reevaluate Handle overflow by capping to DateTime.MaxValue or DateTime.MinValue
                    newTicks = Interlocked.Read(ref _ticks) + ts.Ticks;
                }

                // Since newTicks is being verified within try, this catch block should
                // only be hit if the addition of ts.Ticks causes an overflow near same
                // time, so we can safely assume that newTicks is out of range and
                // handle it accordingly.

                if (newTicks > DateTime.MaxValue.Ticks)
                    newTicks = DateTime.MaxValue.Ticks;
                else if (newTicks < DateTime.MinValue.Ticks)
                    newTicks = DateTime.MinValue.Ticks;
                else
                    throw;  // Re-throw if it's an unexpected overflow

                SafeExchange(newTicks);
            }
            catch
            {
                throw;      // Re-throw it's an unexpected and unknown exception
            }

            return this;
        }
        /// <summary>
        /// Gets a DateTime value converted to local time.
        /// </summary>
        public DateTime ToLocalTime() => this.Value.ToLocalTime();
        /// <summary>
        /// Gets the value of the current DateTime object converted to Coordinated Universal Time (UTC).
        /// </summary>
        public DateTime ToUniversalTime() => this.Value.ToUniversalTime();
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Atomically sets the internal tick value, handling potential overflows by capping to valid DateTime ranges.
        /// </summary>
        /// <remarks>Throws InvalidOperationException if too many attempts are made due to repeated
        /// overflows.</remarks>
        /// <param name="newTicks">The new tick value to set.</param>
        /// <param name="count">The current recursion depth for overflow handling. Defaults to 0.</param>
        /// <returns>The updated tick value after the exchange.</returns>
        private bool SafeExchange(long newTicks, int count = 0)
        {
            try
            {
                if (count > 5) throw new InvalidOperationException("Too many attempts to adjust time, possible overflow scenario.");

                Interlocked.Exchange(ref _ticks, newTicks);
                _dateTime = new DateTime(Interlocked.Read(ref _ticks), (this.IsUTC ? DateTimeKind.Utc : DateTimeKind.Local));
                return true;
            }
            catch (OverflowException)
            {
                // Handle overflow by capping to DateTime.MaxValue or DateTime.MinValue
                if (newTicks > DateTime.MaxValue.Ticks)
                    newTicks = DateTime.MaxValue.Ticks;
                else if (newTicks < DateTime.MinValue.Ticks)
                    newTicks = DateTime.MinValue.Ticks;

                // Recursive call to attempt exchange with the capped value
                return SafeExchange(newTicks, ++count);
            }
            catch
            {
                throw;      // Re-throw it's an unexpected and unknown exception
            }
        }
        #endregion

        #region Operators
        /// <summary>
        /// Determines whether two ADateTime instances are equal.
        /// </summary>
        /// <param name="a">The first ADateTime instance to compare.</param>
        /// <param name="b">The second ADateTime instance to compare.</param>
        /// <returns>true if the instances are equal; otherwise, false.</returns>
        /// <remarks>The use a Value property ensures that the comparison is based on the actual DateTime 
        /// values and its response is faster, because it's not creating new DateTime instances for each comparison.</remarks>
        public static bool operator ==(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : (a.Value.Equals(b.Value));
        /// <summary>
        /// Determines whether two ADateTime instances are not equal.
        /// </summary>
        /// <param name="a">The first ADateTime to compare.</param>
        /// <param name="b">The second ADateTime to compare.</param>
        /// <returns>true if the instances are not equal; otherwise, false.</returns>
        public static bool operator !=(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : !(a.Value.Equals(b.Value));
        /// <summary>
        /// Determines whether one ADateTime instance is earlier than another.
        /// </summary>
        /// <param name="a">The first ADateTime instance to compare.</param>
        /// <param name="b">The second ADateTime instance to compare.</param>
        /// <returns>true if a is earlier than b; otherwise, false.</returns>
        public static bool operator <(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : (a.Value < b.Value);
        /// <summary>
        /// Determines whether one ADateTime instance is later than another.
        /// </summary>
        /// <param name="a">The first ADateTime instance to compare.</param>
        /// <param name="b">The second ADateTime instance to compare.</param>
        /// <returns>true if a is later than b; otherwise, false.</returns>
        public static bool operator >(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : (a.Value > b.Value);
        /// <summary>
        /// Determines whether one ADateTime instance is earlier than or equal to another.
        /// </summary>
        /// <param name="a">The first ADateTime instance to compare.</param>
        /// <param name="b">The second ADateTime instance to compare.</param>
        /// <returns>true if a is earlier than or equal to b; otherwise, false.</returns>
        public static bool operator <=(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : (a.Value <= b.Value);
        /// <summary>
        /// Determines whether one ADateTime instance is later than or equal to another.
        /// </summary>
        /// <param name="a">The first ADateTime instance to compare.</param>
        /// <param name="b">The second ADateTime instance to compare.</param>
        /// <returns>true if a is later than or equal to b; otherwise, false.</returns>
        public static bool operator >=(ADateTime a, ADateTime b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : (a.Value >= b.Value);

        /// <summary>
        /// Converts an ADateTime instance to a DateTime, preserving the UTC or local kind.
        /// </summary>
        /// <remarks>Returns a DateTime with Kind set to Utc if the source is UTC; otherwise, returns a
        /// local DateTime.</remarks>
        /// <param name="obj">The ADateTime instance to convert.</param>
        public static implicit operator DateTime(ADateTime obj) => new DateTime(Interlocked.Read(ref obj._ticks), (obj.IsUTC ? DateTimeKind.Utc : DateTimeKind.Local));
        /// <summary>
        /// Converts a DateTime value to an ADateTime instance.
        /// </summary>
        /// <param name="value">The DateTime value to convert.</param>
        public static implicit operator ADateTime(DateTime value) => new ADateTime(value);
        #endregion

        #region Overrides
        /// <summary>
        /// Determines whether the specified object is equal to the current ADateTime instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current ADateTime instance.</param>
        /// <returns>true if the specified object is an ADateTime and is equal to the current instance; otherwise, false.</returns>
        public override bool Equals(object obj) => (obj is ADateTime adt && Equals(adt)) ||
                                                   (obj is DateTime dt && Equals(dt));
        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode() => Interlocked.Read(ref _ticks).GetHashCode();
        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => Value.ToString();
        #endregion

        #region IEquatable<ADateTime> Implementation
        /// <summary>
        /// Determines whether the current instance and the specified ADateTime object represent the same point in time.
        /// </summary>
        /// <param name="other">The ADateTime object to compare with the current instance.</param>
        /// <returns>true if the objects represent the same point in time; otherwise, false.</returns>
        public bool Equals(ADateTime other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            // Compare the underlying DateTime values for equality, accounting for UTC/local differences
            return this.Equals(other.Value);
        }
        /// <summary>
        /// Determines whether the current DateTime instance represents the same point 
        /// in time as the specified DateTime, accounting for time zone differences.<br/>
        /// DateTime is a struct vs this being a class.  The two are compared by ticks 
        /// and Kind used based on current instance.
        /// </summary>
        /// <param name="other">The DateTime to compare with the current instance.</param>
        /// <returns>true if both instances represent the same point in time; otherwise, false.</returns>
        public bool Equals(DateTime other)
        {
            // Read the Ticks value atomically for both instances
            long a = this.Value.Ticks;
            // Compare Ticks based on the both being UTC or both local kind
            long b = this.IsUTC ? other.ToUniversalTime().Ticks : other.ToLocalTime().Ticks;
            return a == b;
        }
        #endregion
    }
}
