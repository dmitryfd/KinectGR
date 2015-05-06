﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Kinect;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using System.Drawing;
using System.Windows;
using System.Windows.Media.TextFormatting;
using Point = System.Drawing.Point;
using PointF = System.Drawing.PointF;

namespace ThesisProj
{
    //public class Finger
    //{
    //    public PointF Tip;
    //    public Rect Position;
    //}

    public enum Direction
    {
        DirectionUnknown,
        DirectionLeft,
        DirectionRight,
        DirectionUp,
        DirectionDown
    }

    public class Finger
    {
        public double Width;
        public PointF Tip;
        public PointF Base;
    }

    public class Hand
    {
        private const double OuterCircleMultiplier = 1.75;

        private Point _handJoint;
        private Point _wristJoint;
        private Point _handtipJoint;
        private Point _thumbJoint;

        private byte[] _fingersMask;

        public Rect Position;
        public bool[] Mask;

        public Image<Gray, byte> MaskImage;
        public Image<Bgr, byte> DisplayImage;

        public Point PalmCenter;
        public int InnerCircleRadius;
        public int OuterCircleRadius;
        public Direction Direction;
        public List<Finger> Fingers;

        public Hand(Rect position, Dictionary<String, Joint> joints, bool[] mask)
        {
            Position = position;
            Mask = mask;
            _fingersMask = new byte[Utility.HandWidth * Utility.HandHeight];

            _handJoint = Utility.ConvertFrameToResizedMaskCoordinate(joints["hand"].Position, Position);
            _wristJoint = Utility.ConvertFrameToResizedMaskCoordinate(joints["wrist"].Position, Position);
            _handtipJoint = Utility.ConvertFrameToResizedMaskCoordinate(joints["handtip"].Position, Position);
            _thumbJoint = Utility.ConvertFrameToResizedMaskCoordinate(joints["thumb"].Position, Position);

            Analyze();
        }

        public void Analyze()
        {
            MaskImage = Utility.ConvertMaskToImage(Mask, Utility.HandWidth, Utility.HandHeight);
            DisplayImage = MaskImage.Copy().Convert<Bgr, byte>();

            PalmCenter = LocatePalmCenter();
            InnerCircleRadius = CalculateInnerRadius();
            OuterCircleRadius = (int)(InnerCircleRadius * OuterCircleMultiplier);
            Fingers = ExtractFingers();
            Direction = CalculateDirection();

            // Draw center
            DisplayImage.Draw(new CircleF(PalmCenter, 5), new Bgr(Color.Red), -3);

            // Draw inner circle
            DisplayImage.Draw(new CircleF(PalmCenter, InnerCircleRadius), new Bgr(Color.Orange), 5);

            // Draw outer circle
            //DisplayImage.Draw(new CircleF(PalmCenter, OuterCircleRadius), new Bgr(Color.Yellow), 3);

            // Draw Kinect wrist joint
            //DisplayImage.Draw(new CircleF(_wristJoint, 5), new Bgr(Color.Red), -3);

            foreach (Finger f in Fingers)
            {
                // Draw finger bones
                DisplayImage.Draw(new LineSegment2DF(f.Base, f.Tip), new Bgr(Color.DarkCyan), 3);

                // Draw finger tips
                DisplayImage.Draw(new CircleF(f.Tip, 5), new Bgr(Color.Cyan), -3);
            }
        }

        private Point LocatePalmCenter()
        {
            double[] dt = DistanceTransform2D();
            double max = Double.NegativeInfinity;

            // Find maximum Euclidean distance
            for (int y = 0; y < Utility.HandHeight; ++y)
            {
                for (int x = 0; x < Utility.HandWidth; ++x)
                {
                    if (dt[y * Utility.HandWidth + x] > max)
                    {
                        max = dt[y * Utility.HandWidth + x];
                    }
                }
            }

            // Retrieve possible palm candidates
            List<Point> candidates = new List<Point>();
            for (int y = 0; y < Utility.HandHeight; ++y)
            {
                for (int x = 0; x < Utility.HandWidth; ++x)
                {
                    if (Math.Abs(dt[y * Utility.HandWidth + x] - max) < 0.1)
                    {
                        candidates.Add(new Point(x, y));
                    }
                }
            }

            double rx = 0;
            double ry = 0;

            for (int i = 0; i < candidates.Count; ++i)
            {
                rx += candidates[i].X;
                ry += candidates[i].Y;
            }
            rx /= candidates.Count;
            ry /= candidates.Count;

            return new Point((int)rx, (int)ry);
        }

        public int CalculateInnerRadius()
        {
            int r = 1;

            while (IsValidInnerRadius(r))
            {
                r = r + 1;
            }

            return r;
        }

        private bool IsValidInnerRadius(int r)
        {
            if (PalmCenter.X + r >= Utility.HandWidth
                || PalmCenter.X - r < 0
                || PalmCenter.Y + r >= Utility.HandHeight
                || PalmCenter.Y - r < 0)
            {
                return false;
            }

            const int SAMPLE_COUNT = 180;
            const double INTERVAL = (360.0 / SAMPLE_COUNT) * (Math.PI / 200);

            double alpha = 0;
            for (int i = 0; i < SAMPLE_COUNT; i++)
            {
                int x = (int)Math.Floor(PalmCenter.X + r * Math.Cos(alpha));
                int y = (int)Math.Floor(PalmCenter.Y + r * Math.Sin(alpha));

                if (Mask[y * Utility.HandWidth + x] == false)
                {
                    return false;
                }

                alpha += INTERVAL;
            }

            return true;
        }

        private double[] DistanceTransform1D(double[] arr, int n)
        {
            int[] v = new int[n];
            double[] z = new double[n + 1];
            int k = 0;

            v[0] = 0;
            z[0] = Double.NegativeInfinity;
            z[1] = Double.PositiveInfinity;

            double s;
            for (int i = 1; i < n; i++)
            {
                s = ((arr[i] + i * i) - (arr[v[k]] + v[k] * v[k])) / (2.0 * i - 2.0 * v[k]);

                while (s <= z[k])
                {
                    --k;
                    s = ((arr[i] + i * i) - (arr[v[k]] + v[k] * v[k])) / (2.0 * i - 2.0 * v[k]);
                }

                ++k;
                v[k] = i;
                z[k] = s;
                z[k + 1] = Double.PositiveInfinity;
            }

            k = 0;
            double[] result = new double[n];

            for (int i = 0; i < n; i++)
            {
                while (z[k + 1] < i) ++k;
                result[i] = ((i - v[k]) * (i - v[k]) + arr[v[k]]);
            }

            v = null;
            z = null;
            return result;
        }

        private double[] DistanceTransform2D()
        {
            double[] result = new double[Utility.HandWidth * Utility.HandHeight];
            double[] tmp = new double[Math.Max(Utility.HandWidth, Utility.HandHeight)];

            // For columns
            for (int x = 0; x < Utility.HandWidth; ++x)
            {
                for (int y = 0; y < Utility.HandHeight; ++y)
                {
                    tmp[y] = Mask[y * Utility.HandWidth + x] ? Double.MaxValue - 1 : 0;
                }

                double[] d = DistanceTransform1D(tmp, Utility.HandHeight);

                for (int y = 0; y < Utility.HandHeight; y++)
                {
                    result[y * Utility.HandWidth + x] = d[y];
                }
            }

            // For rows
            for (int y = 0; y < Utility.HandHeight; y++)
            {
                for (int x = 0; x < Utility.HandWidth; x++)
                {
                    tmp[x] = result[y * Utility.HandWidth + x];
                }

                double[] d = DistanceTransform1D(tmp, Utility.HandWidth);

                for (int x = 0; x < Utility.HandWidth; x++)
                {
                    result[y * Utility.HandWidth + x] = d[x];
                }
            }

            tmp = null;
            return result;
        }

        public Direction CalculateDirection()
        {
            if (Fingers == null || Fingers.Count == 0)
            {
                return Direction.DirectionUnknown;
            }
            
            PointF up = new PointF((float)(Utility.HandWidth/2.0), 0);
            PointF down = new PointF((float)(Utility.HandWidth / 2.0), Utility.HandHeight);
            PointF left = new PointF(0, (float)(Utility.HandHeight / 2.0));
            PointF right = new PointF(Utility.HandWidth, (float)(Utility.HandHeight / 2.0));

            float px = 0, py = 0;
            foreach (Finger f in Fingers)
            {
                px += f.Tip.X;
                py += f.Tip.Y;
            }

            px = px/Fingers.Count;
            py = py/Fingers.Count;
            PointF p = new PointF(px, py);

            double distUp = Utility.Dist(p, up);
            double distDown = Utility.Dist(p, down);
            double distLeft = Utility.Dist(p, left);
            double distRight = Utility.Dist(p, right);
            double min = (new double[] {distUp, distDown, distLeft, distRight}).Min();

            if (Utility.IsEq(min, distUp))
            {
                return Direction.DirectionUp;
            }

            if (Utility.IsEq(min, distDown))
            {
                return Direction.DirectionDown;
            }

            if (Utility.IsEq(min, distLeft))
            {
                return Direction.DirectionLeft;
            }

            if (Utility.IsEq(min, distRight))
            {
                return Direction.DirectionRight;
            }

            return Direction.DirectionUnknown;
        }

        public List<Finger> ExtractFingers()
        {
            Array.Clear(_fingersMask, 0, _fingersMask.Length);
            List<Finger> fingers = new List<Finger>();
            int fingerCount = 0;

            int r = (int)Math.Floor(InnerCircleRadius * OuterCircleMultiplier);
            byte i = 0;

            for (int yi = 0; yi < Utility.HandHeight; ++yi)
            {
                for (int xi = 0; xi < Utility.HandWidth; ++xi)
                {
                    if ((PalmCenter.X - xi) * (PalmCenter.X - xi) + (PalmCenter.Y - yi) * (PalmCenter.Y - yi) >= r * r
                        && Mask[yi * Utility.HandWidth + xi]
                        && _fingersMask[yi * Utility.HandWidth + xi] == 0)
                    {
                        // A candidate pixel. Expand it to a region.
                        bool[] candidate = ExtractRegion(xi, yi, ++i);

                        if (candidate != null)
                        {
                            Rect position = CalculateFingerPosition(candidate);
                            PointF fingertip = CalculateFingerTip(candidate, position);
                            Tuple<Point, Point, double> baseLine = CalculateFingerBase(candidate, position);

                            double baseWidth = baseLine.Item3;

                            // Something is wrong...
                            if (baseWidth <= 1)
                            {
                                continue;
                            }

                            // Ignore if wrist.
                            if (Utility.Dist(_wristJoint, fingertip) < OuterCircleRadius / 2.0)
                            {
                                continue;
                            }

                            double refWidth = (double)InnerCircleRadius * 1.7;

                            double ratio = baseWidth * 4 / refWidth;

                            int count = 1;
                            if (ratio >= 1.4 && ratio < 2.4) count = 2;
                            else if (ratio >= 2.4 && ratio < 3.4) count = 3;
                            else if (ratio >= 3.4 && ratio < 5.4) count = 4;

                            fingerCount += count;
                            if (fingerCount > 5)
                            {
                                // Unreliable in this case.
                                return new List<Finger>();
                            }

                            // Calculate implied fingers
                            PointF p1 = baseLine.Item1;
                            PointF p2 = baseLine.Item2;

                            if (Utility.MagicalFunction(p2, p1, fingertip) < 0)
                            {
                                PointF tmp = p1;
                                p1 = p2;
                                p2 = tmp;
                            }

                            double xv = p1.X - p2.X;
                            double yv = p1.Y - p2.Y;
                            double length = Math.Sqrt(xv * xv + yv * yv);

                            xv = xv / length;
                            yv = yv / length;

                            double xp = -yv;
                            double yp = xv;

                            xp = xp / Math.Sqrt(xp * xp + yp * yp);
                            yp = yp / Math.Sqrt(xp * xp + yp * yp);

                            // Save fingers
                            double finWidth = baseWidth / count;
                            for (int j = 0; j < count; ++j)
                            {
                                double d = finWidth / 2 + j * finWidth;

                                double sx = p2.X + d * xv;
                                double sy = p2.Y + d * yv;
                                PointF basePoint = new PointF((float)sx, (float)sy);

                                double dist = Utility.Dist(basePoint, fingertip);
                                double fx = sx + dist * xp;
                                double fy = sy + dist * yp;
                                PointF localFingertip = new PointF((float)fx, (float)fy);


                                Finger f = new Finger();
                                f.Width = finWidth;
                                f.Base = basePoint;
                                f.Tip = fingertip; // localFingertip?

                                fingers.Add(f);
                            }
                        }
                    }
                }
            }

            return fingers;
        }

        private bool[] ExtractRegion(int xi, int yi, byte id)
        {
            bool[] region = new bool[Utility.HandWidth * Utility.HandHeight];
            int r = (int)Math.Floor(InnerCircleRadius * OuterCircleMultiplier);
            int area = 0;

            Vector n = new Vector(_handJoint.X - _wristJoint.X, _handJoint.Y - _wristJoint.Y);

            Queue<Point> queue = new Queue<Point>();
            queue.Enqueue(new Point(xi, yi));

            Point p;
            while (queue.Count > 0)
            {
                p = queue.Dequeue();
                int x = (int)p.X;
                int y = (int)p.Y;

                if (region[y * Utility.HandWidth + x] || _fingersMask[y * Utility.HandWidth + x] != 0)
                {
                    // Already visited.
                    continue;
                }

                //Vector q = new Vector(x - _wristJoint.X, y - _wristJoint.Y);
                //if (n.X * q.X + n.Y * q.Y <= 0)
                //{
                //    // Over the wrist.
                //    return null;
                //}

                region[y * Utility.HandWidth + x] = true;
                ++area;

                if (y + 1 < Utility.HandWidth
                    && Mask[(y + 1) * Utility.HandWidth + x]
                    && (PalmCenter.X - x) * (PalmCenter.X - x) + (PalmCenter.Y - y + 1) * (PalmCenter.Y - y + 1) >= r * r)
                {
                    queue.Enqueue(new Point(x, y + 1));
                }

                if (y - 1 > 0
                    && Mask[(y - 1) * Utility.HandWidth + x]
                    && (PalmCenter.X - x) * (PalmCenter.X - x) + (PalmCenter.Y - y - 1) * (PalmCenter.Y - y - 1) >= r * r)
                {
                    queue.Enqueue(new Point(x, y - 1));
                }

                if (x + 1 < Utility.HandHeight
                    && Mask[y * Utility.HandWidth + x + 1]
                    && (PalmCenter.X - x + 1) * (PalmCenter.X - x + 1) + (PalmCenter.Y - y) * (PalmCenter.Y - y) >= r * r)
                {
                    queue.Enqueue(new Point(x + 1, y));
                }

                if (x - 1 > 0
                    && Mask[y * Utility.HandWidth + x - 1]
                    && (PalmCenter.X - x - 1) * (PalmCenter.X - x - 1) + (PalmCenter.Y - y) * (PalmCenter.Y - y) >= r * r)
                {
                    queue.Enqueue(new Point(x - 1, y));
                }
            }

            for (int i = 0; i < region.Length; ++i)
            {
                if (region[i])
                {
                    _fingersMask[i] = id;
                }
            }

            return region;
        }

        private Rect CalculateFingerPosition(bool[] mask)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            for (int yi = 0; yi < Utility.HandHeight; ++yi)
            {
                for (int xi = 0; xi < Utility.HandWidth; ++xi)
                {
                    if (mask[yi * Utility.HandWidth + xi])
                    {
                        if (xi < minX)
                        {
                            minX = xi;
                        }
                        if (xi > maxX)
                        {
                            maxX = xi;
                        }
                        if (yi < minY)
                        {
                            minY = yi;
                        }
                        if (yi > maxY)
                        {
                            maxY = yi;
                        }
                    }
                }
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private PointF CalculateFingerTip(bool[] mask, Rect position)
        {
            double max = Double.MinValue;
            int x = 0;
            int y = 0;

            double d;
            for (int yi = position.Y; yi < position.Y + position.Height; ++yi)
            {
                for (int xi = position.X; xi < position.X + position.Width; ++xi)
                {
                    if (mask[yi * Utility.HandWidth + xi])
                    {
                        d = (xi - PalmCenter.X) * (xi - PalmCenter.X) + (yi - PalmCenter.Y) * (yi - PalmCenter.Y);
                        if (d > max)
                        {
                            max = d;
                            x = xi;
                            y = yi;
                        }
                    }
                }
            }

            return new PointF(x, y);
        }

        private Tuple<Point, Point, double> CalculateFingerBase(bool[] mask, Rect position)
        {
            int r = (int)Math.Floor(InnerCircleRadius * OuterCircleMultiplier);
            List<Point> candidates = new List<Point>();

            for (int yi = position.Y; yi < position.Y + position.Height; ++yi)
            {
                for (int xi = position.X; xi < position.X + position.Width; ++xi)
                {
                    bool neighbor = false;
                    if (mask[yi * Utility.HandWidth + xi])
                    {
                        for (int i = -1; i < 2; ++i)
                        {
                            if (xi + i >= 0 && xi + i < Utility.HandWidth)
                            {
                                if ((PalmCenter.X - xi + i) * (PalmCenter.X - xi + i) +
                                    (PalmCenter.Y - yi) * (PalmCenter.Y - yi) <= r * r)
                                {
                                    neighbor = true;
                                }
                            }

                            if (yi + i >= 0 && yi + i < Utility.HandHeight)
                            {
                                if ((PalmCenter.X - xi) * (PalmCenter.X - xi) +
                                    (PalmCenter.Y - yi + i) * (PalmCenter.Y - yi + i) <= r * r)
                                {
                                    neighbor = true;
                                }
                            }
                        }
                    }

                    if (neighbor)
                    {
                        candidates.Add(new Point(xi, yi));
                    }
                }
            }

            if (candidates.Count < 2)
            {
                return new Tuple<Point, Point, double>(new Point(), new Point(), -1);
            }

            candidates.Sort((x, y) => x.X > y.X ? -1 : x.X < y.X ? 1 : x.Y > y.Y ? -1 : x.Y < y.Y ? 1 : 0);

            return new Tuple<Point, Point, double>(candidates[0], candidates[candidates.Count - 1], Utility.Dist(candidates[0], candidates[candidates.Count - 1]));
        }
    }
}