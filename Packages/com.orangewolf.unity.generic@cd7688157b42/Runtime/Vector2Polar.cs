using System;

namespace OrangeWolf.Generic
{
    public struct Vector2Polar
    {
        public double Radius { get; set; } // Distance from the origin
        public double Angle { get; set; } // In degrees
        
        public Vector2Polar(double radius, double angle)
        {
            Radius = radius;
            Angle = angle;
        }
        
        public static Vector2Polar CartesianToPolar(float x, float y, float centerX, float centerY)
        {
            // Calculate the displacement from the center
            double dx = x - centerX;
            double dy = y - centerY;

            // Calculate the radius and angle
            double radius = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx) * (180 / Math.PI); // Convert radians to degrees

            // Ensure angle is in the range [0, 360)
            if (angle < 0)
                angle += 360;

            return new Vector2Polar(radius, angle);
        }
        
        public static (double x, double y) PolarToCartesian(double radius, double angleInDegrees, double centerX, double centerY)
        {
            double angleInRadians = angleInDegrees * Math.PI / 180.0;
            double x = centerX + radius * Math.Cos(angleInRadians);
            double y = centerY + radius * Math.Sin(angleInRadians);
            return (x, y);
        }
    }
}