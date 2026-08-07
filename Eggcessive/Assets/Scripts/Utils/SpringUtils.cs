/******************************************************************************
  Based on the exact damped spring solution by Ryan Juckett.
  http://www.ryanjuckett.com/

  Copyright (c) 2008-2012 Ryan Juckett
  Altered and expanded for C# / Unity use.
******************************************************************************/

using System;
using UnityEngine;

/// <summary>
/// Allocation-free, frame-rate-independent damped springs for common Unity
/// value types. Frequencies are expressed in cycles per second, damping is a
/// ratio (less than one oscillates, one is critical, greater than one is
/// overdamped), impulses are value-units per second, and forces are value-units
/// times mass per second squared.
/// </summary>
public static class SpringUtils
{
    private const float Epsilon = 0.0001f;
    private const float TwoPi = Mathf.PI * 2f;

    public readonly struct MotionParams
    {
        internal readonly float PositionPosition;
        internal readonly float PositionVelocity;
        internal readonly float VelocityPosition;
        internal readonly float VelocityVelocity;
        internal readonly bool IsActive;

        internal MotionParams(
            float positionPosition,
            float positionVelocity,
            float velocityPosition,
            float velocityVelocity,
            bool isActive = true)
        {
            PositionPosition = positionPosition;
            PositionVelocity = positionVelocity;
            VelocityPosition = velocityPosition;
            VelocityVelocity = velocityVelocity;
            IsActive = isActive;
        }

        public static MotionParams Identity =>
            new MotionParams(1f, 0f, 0f, 1f, false);
    }

    [Serializable]
    public struct FloatSpring
    {
        public float Value;
        public float Velocity;

        public FloatSpring(float value, float velocity = 0f)
        {
            Value = value;
            Velocity = velocity;
        }

        public void Reset(float value, float velocity = 0f)
        {
            Value = value;
            Velocity = velocity;
        }

        public void AddImpulse(float impulse)
        {
            Velocity += impulse;
        }

        public void AddForce(
            float force,
            float deltaTime,
            float mass = 1f)
        {
            SpringUtils.AddForce(ref Velocity, force, deltaTime, mass);
        }

        public float Update(
            float target,
            float deltaTime,
            float frequencyHz,
            float dampingRatio,
            float targetVelocity = 0f)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                frequencyHz,
                dampingRatio);
            return Value;
        }

        public float Update(
            float target,
            float targetVelocity,
            float deltaTime,
            in MotionParams motion)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                motion);
            return Value;
        }

        public void ClampValue(float minimum, float maximum)
        {
            SpringUtils.ClampValue(
                ref Value,
                ref Velocity,
                minimum,
                maximum);
        }

        public void ClampVelocity(float maximumMagnitude)
        {
            Velocity = Mathf.Clamp(
                Velocity,
                -Mathf.Abs(maximumMagnitude),
                Mathf.Abs(maximumMagnitude));
        }
    }

    [Serializable]
    public struct Vector2Spring
    {
        public Vector2 Value;
        public Vector2 Velocity;

        public Vector2Spring(Vector2 value, Vector2 velocity = default)
        {
            Value = value;
            Velocity = velocity;
        }

        public void Reset(Vector2 value, Vector2 velocity = default)
        {
            Value = value;
            Velocity = velocity;
        }

        public void AddImpulse(Vector2 impulse)
        {
            Velocity += impulse;
        }

        public void AddForce(
            Vector2 force,
            float deltaTime,
            float mass = 1f)
        {
            SpringUtils.AddForce(ref Velocity, force, deltaTime, mass);
        }

        public Vector2 Update(
            Vector2 target,
            float deltaTime,
            float frequencyHz,
            float dampingRatio,
            Vector2 targetVelocity = default)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                frequencyHz,
                dampingRatio);
            return Value;
        }

        public Vector2 Update(
            Vector2 target,
            Vector2 targetVelocity,
            float deltaTime,
            in MotionParams motion)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                motion);
            return Value;
        }

        public void ClampValue(Vector2 minimum, Vector2 maximum)
        {
            SpringUtils.ClampValue(
                ref Value.x,
                ref Velocity.x,
                minimum.x,
                maximum.x);
            SpringUtils.ClampValue(
                ref Value.y,
                ref Velocity.y,
                minimum.y,
                maximum.y);
        }

        public void ClampVelocity(float maximumMagnitude)
        {
            Velocity = Vector2.ClampMagnitude(
                Velocity,
                Mathf.Max(0f, maximumMagnitude));
        }
    }

    [Serializable]
    public struct Vector3Spring
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public Vector3Spring(Vector3 value, Vector3 velocity = default)
        {
            Value = value;
            Velocity = velocity;
        }

        public void Reset(Vector3 value, Vector3 velocity = default)
        {
            Value = value;
            Velocity = velocity;
        }

        public void AddImpulse(Vector3 impulse)
        {
            Velocity += impulse;
        }

        public void AddForce(
            Vector3 force,
            float deltaTime,
            float mass = 1f)
        {
            SpringUtils.AddForce(ref Velocity, force, deltaTime, mass);
        }

        public Vector3 Update(
            Vector3 target,
            float deltaTime,
            float frequencyHz,
            float dampingRatio,
            Vector3 targetVelocity = default)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                frequencyHz,
                dampingRatio);
            return Value;
        }

        public Vector3 Update(
            Vector3 target,
            Vector3 targetVelocity,
            float deltaTime,
            in MotionParams motion)
        {
            Step(
                ref Value,
                ref Velocity,
                target,
                targetVelocity,
                deltaTime,
                motion);
            return Value;
        }

        public void ClampValue(Vector3 minimum, Vector3 maximum)
        {
            SpringUtils.ClampValue(
                ref Value.x,
                ref Velocity.x,
                minimum.x,
                maximum.x);
            SpringUtils.ClampValue(
                ref Value.y,
                ref Velocity.y,
                minimum.y,
                maximum.y);
            SpringUtils.ClampValue(
                ref Value.z,
                ref Velocity.z,
                minimum.z,
                maximum.z);
        }

        public void ClampVelocity(float maximumMagnitude)
        {
            Velocity = Vector3.ClampMagnitude(
                Velocity,
                Mathf.Max(0f, maximumMagnitude));
        }
    }

    [Serializable]
    public struct AngleSpring
    {
        public float Value;
        public float Velocity;

        public AngleSpring(float valueDegrees, float velocity = 0f)
        {
            Value = valueDegrees;
            Velocity = velocity;
        }

        public void Reset(float valueDegrees, float velocity = 0f)
        {
            Value = valueDegrees;
            Velocity = velocity;
        }

        public void AddImpulse(float impulseDegreesPerSecond)
        {
            Velocity += impulseDegreesPerSecond;
        }

        public float Update(
            float targetDegrees,
            float deltaTime,
            float frequencyHz,
            float dampingRatio,
            float targetVelocity = 0f)
        {
            StepAngle(
                ref Value,
                ref Velocity,
                targetDegrees,
                targetVelocity,
                deltaTime,
                frequencyHz,
                dampingRatio);
            return Value;
        }

        public float Update(
            float targetDegrees,
            float targetVelocity,
            float deltaTime,
            in MotionParams motion)
        {
            StepAngle(
                ref Value,
                ref Velocity,
                targetDegrees,
                targetVelocity,
                deltaTime,
                motion);
            return Value;
        }

        public void ClampValue(float minimum, float maximum)
        {
            SpringUtils.ClampValue(
                ref Value,
                ref Velocity,
                minimum,
                maximum);
        }

        public void ClampVelocity(float maximumMagnitude)
        {
            Velocity = Mathf.Clamp(
                Velocity,
                -Mathf.Abs(maximumMagnitude),
                Mathf.Abs(maximumMagnitude));
        }
    }

    public static MotionParams CalculateMotionParams(
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        return CalculateAngularMotionParams(
            deltaTime,
            Mathf.Max(0f, frequencyHz) * TwoPi,
            dampingRatio);
    }

    public static MotionParams CalculateMotionParamsFromStiffness(
        float deltaTime,
        float stiffness,
        float dampingRatio,
        float mass = 1f)
    {
        float angularFrequency = mass > Epsilon
            ? Mathf.Sqrt(Mathf.Max(0f, stiffness) / mass)
            : 0f;
        return CalculateAngularMotionParams(
            deltaTime,
            angularFrequency,
            dampingRatio);
    }

    public static void Step(
        ref float value,
        ref float velocity,
        float target,
        float targetVelocity,
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        MotionParams motion = CalculateMotionParams(
            deltaTime,
            frequencyHz,
            dampingRatio);
        Step(
            ref value,
            ref velocity,
            target,
            targetVelocity,
            deltaTime,
            motion);
    }

    public static void Step(
        ref Vector2 value,
        ref Vector2 velocity,
        Vector2 target,
        Vector2 targetVelocity,
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        MotionParams motion = CalculateMotionParams(
            deltaTime,
            frequencyHz,
            dampingRatio);
        Step(
            ref value,
            ref velocity,
            target,
            targetVelocity,
            deltaTime,
            motion);
    }

    public static void Step(
        ref Vector3 value,
        ref Vector3 velocity,
        Vector3 target,
        Vector3 targetVelocity,
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        MotionParams motion = CalculateMotionParams(
            deltaTime,
            frequencyHz,
            dampingRatio);
        Step(
            ref value,
            ref velocity,
            target,
            targetVelocity,
            deltaTime,
            motion);
    }

    public static void StepAngle(
        ref float valueDegrees,
        ref float velocity,
        float targetDegrees,
        float targetVelocity,
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        float unwrappedTarget = valueDegrees
            + Mathf.DeltaAngle(valueDegrees, targetDegrees);
        Step(
            ref valueDegrees,
            ref velocity,
            unwrappedTarget,
            targetVelocity,
            deltaTime,
            frequencyHz,
            dampingRatio);
        valueDegrees = Mathf.Repeat(valueDegrees + 180f, 360f) - 180f;
    }

    public static void StepAngle(
        ref float valueDegrees,
        ref float velocity,
        float targetDegrees,
        float targetVelocity,
        float deltaTime,
        in MotionParams motion)
    {
        float unwrappedTarget = valueDegrees
            + Mathf.DeltaAngle(valueDegrees, targetDegrees);
        Step(
            ref valueDegrees,
            ref velocity,
            unwrappedTarget,
            targetVelocity,
            deltaTime,
            motion);
        valueDegrees = Mathf.Repeat(valueDegrees + 180f, 360f) - 180f;
    }

    public static void StepEulerAngles(
        ref Vector3 valueDegrees,
        ref Vector3 velocity,
        Vector3 targetDegrees,
        Vector3 targetVelocity,
        float deltaTime,
        float frequencyHz,
        float dampingRatio)
    {
        StepAngle(
            ref valueDegrees.x,
            ref velocity.x,
            targetDegrees.x,
            targetVelocity.x,
            deltaTime,
            frequencyHz,
            dampingRatio);
        StepAngle(
            ref valueDegrees.y,
            ref velocity.y,
            targetDegrees.y,
            targetVelocity.y,
            deltaTime,
            frequencyHz,
            dampingRatio);
        StepAngle(
            ref valueDegrees.z,
            ref velocity.z,
            targetDegrees.z,
            targetVelocity.z,
            deltaTime,
            frequencyHz,
            dampingRatio);
    }

    public static void StepEulerAngles(
        ref Vector3 valueDegrees,
        ref Vector3 velocity,
        Vector3 targetDegrees,
        Vector3 targetVelocity,
        float deltaTime,
        in MotionParams motion)
    {
        StepAngle(
            ref valueDegrees.x,
            ref velocity.x,
            targetDegrees.x,
            targetVelocity.x,
            deltaTime,
            motion);
        StepAngle(
            ref valueDegrees.y,
            ref velocity.y,
            targetDegrees.y,
            targetVelocity.y,
            deltaTime,
            motion);
        StepAngle(
            ref valueDegrees.z,
            ref velocity.z,
            targetDegrees.z,
            targetVelocity.z,
            deltaTime,
            motion);
    }

    public static void AddForce(
        ref float velocity,
        float force,
        float deltaTime,
        float mass = 1f)
    {
        if (deltaTime > 0f && mass > Epsilon)
        {
            velocity += force / mass * deltaTime;
        }
    }

    public static void AddForce(
        ref Vector2 velocity,
        Vector2 force,
        float deltaTime,
        float mass = 1f)
    {
        if (deltaTime > 0f && mass > Epsilon)
        {
            velocity += force / mass * deltaTime;
        }
    }

    public static void AddForce(
        ref Vector3 velocity,
        Vector3 force,
        float deltaTime,
        float mass = 1f)
    {
        if (deltaTime > 0f && mass > Epsilon)
        {
            velocity += force / mass * deltaTime;
        }
    }

    public static void ClampValue(
        ref float value,
        ref float velocity,
        float minimum,
        float maximum)
    {
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        if (value < minimum)
        {
            value = minimum;
            velocity = Mathf.Max(0f, velocity);
        }
        else if (value > maximum)
        {
            value = maximum;
            velocity = Mathf.Min(0f, velocity);
        }
    }

    public static void Step(
        ref float value,
        ref float velocity,
        float target,
        float targetVelocity,
        float deltaTime,
        in MotionParams motion)
    {
        if (deltaTime <= 0f || !motion.IsActive)
        {
            return;
        }

        float relativePosition = value - target;
        float relativeVelocity = velocity - targetVelocity;
        value = relativePosition * motion.PositionPosition
            + relativeVelocity * motion.PositionVelocity
            + target
            + targetVelocity * deltaTime;
        velocity = relativePosition * motion.VelocityPosition
            + relativeVelocity * motion.VelocityVelocity
            + targetVelocity;
    }

    public static void Step(
        ref Vector2 value,
        ref Vector2 velocity,
        Vector2 target,
        Vector2 targetVelocity,
        float deltaTime,
        in MotionParams motion)
    {
        if (deltaTime <= 0f || !motion.IsActive)
        {
            return;
        }

        Vector2 relativePosition = value - target;
        Vector2 relativeVelocity = velocity - targetVelocity;
        value = relativePosition * motion.PositionPosition
            + relativeVelocity * motion.PositionVelocity
            + target
            + targetVelocity * deltaTime;
        velocity = relativePosition * motion.VelocityPosition
            + relativeVelocity * motion.VelocityVelocity
            + targetVelocity;
    }

    public static void Step(
        ref Vector3 value,
        ref Vector3 velocity,
        Vector3 target,
        Vector3 targetVelocity,
        float deltaTime,
        in MotionParams motion)
    {
        if (deltaTime <= 0f || !motion.IsActive)
        {
            return;
        }

        Vector3 relativePosition = value - target;
        Vector3 relativeVelocity = velocity - targetVelocity;
        value = relativePosition * motion.PositionPosition
            + relativeVelocity * motion.PositionVelocity
            + target
            + targetVelocity * deltaTime;
        velocity = relativePosition * motion.VelocityPosition
            + relativeVelocity * motion.VelocityVelocity
            + targetVelocity;
    }

    private static MotionParams CalculateAngularMotionParams(
        float deltaTime,
        float angularFrequency,
        float dampingRatio)
    {
        if (deltaTime <= 0f || angularFrequency < Epsilon)
        {
            return MotionParams.Identity;
        }

        angularFrequency = Mathf.Max(0f, angularFrequency);
        dampingRatio = Mathf.Max(0f, dampingRatio);
        if (dampingRatio > 1f + Epsilon)
        {
            float za = -angularFrequency * dampingRatio;
            float zb = angularFrequency * Mathf.Sqrt(
                dampingRatio * dampingRatio - 1f);
            float z1 = za - zb;
            float z2 = za + zb;
            float e1 = Mathf.Exp(z1 * deltaTime);
            float e2 = Mathf.Exp(z2 * deltaTime);
            float inverseTwoZb = 1f / (2f * zb);
            float e1OverTwoZb = e1 * inverseTwoZb;
            float e2OverTwoZb = e2 * inverseTwoZb;
            float z1e1OverTwoZb = z1 * e1OverTwoZb;
            float z2e2OverTwoZb = z2 * e2OverTwoZb;
            return new MotionParams(
                e1OverTwoZb * z2 - z2e2OverTwoZb + e2,
                -e1OverTwoZb + e2OverTwoZb,
                (z1e1OverTwoZb - z2e2OverTwoZb + e2) * z2,
                -z1e1OverTwoZb + z2e2OverTwoZb);
        }

        if (dampingRatio < 1f - Epsilon)
        {
            float omegaZeta = angularFrequency * dampingRatio;
            float alpha = angularFrequency * Mathf.Sqrt(
                1f - dampingRatio * dampingRatio);
            float exponential = Mathf.Exp(-omegaZeta * deltaTime);
            float cosine = Mathf.Cos(alpha * deltaTime);
            float sine = Mathf.Sin(alpha * deltaTime);
            float exponentialSine = exponential * sine;
            float exponentialCosine = exponential * cosine;
            float omegaSineOverAlpha = exponential
                * omegaZeta * sine / alpha;
            return new MotionParams(
                exponentialCosine + omegaSineOverAlpha,
                exponentialSine / alpha,
                -exponentialSine * alpha
                    - omegaZeta * omegaSineOverAlpha,
                exponentialCosine - omegaSineOverAlpha);
        }

        float criticalExponential = Mathf.Exp(
            -angularFrequency * deltaTime);
        float timeExponential = deltaTime * criticalExponential;
        float timeFrequency = timeExponential * angularFrequency;
        return new MotionParams(
            timeFrequency + criticalExponential,
            timeExponential,
            -angularFrequency * timeFrequency,
            -timeFrequency + criticalExponential);
    }
}
