using System;
using System.Drawing;

namespace DesktopCat
{
    public sealed class CatWindowInput
    {
        public double DragThresholdPx = 6;
        public double VelocityWindow = 0.08;
        public double MaxReleaseSpeed = 2500;
        public double PetWindow = 1.2;
        public double PetHold = 0.7;
        public double DoubleClickTime = 0.5;
        public double DoubleClickDistPx = 6;

        bool pressed, dragging, pressIsDouble;
        Point pressPt;
        double lastClickTime = -10;
        Point lastClickPt;

        bool edgeClicked, edgeDouble, edgeReleased;
        PointF releaseVel;

        const int HistN = 32;
        readonly double[] histT = new double[HistN];
        readonly int[] histX = new int[HistN];
        readonly int[] histY = new int[HistN];
        int histCount, histHead;

        const int PetN = 96;
        readonly double[] petT = new double[PetN];
        readonly int[] petX = new int[PetN];
        readonly int[] petY = new int[PetN];
        int petCount, petHead;
        double lastOverCat = -10, lastNear = -10, pettingUntil = -10;

        public bool Pressed { get { return pressed; } }
        public bool Dragging { get { return dragging; } }

        public void MouseDown(double t, Point p, bool overCat, bool dblMessage)
        {
            if (!overCat) return;
            pressed = true;
            dragging = false;
            pressPt = p;
            bool dbl = dblMessage ||
                       (t - lastClickTime <= DoubleClickTime &&
                        Math.Abs(p.X - lastClickPt.X) <= DoubleClickDistPx && Math.Abs(p.Y - lastClickPt.Y) <= DoubleClickDistPx);
            pressIsDouble = dbl;
            AddHist(t, p);
            ClearPet();
        }

        public void MouseMove(double t, Point p)
        {
            AddHist(t, p);
            if (pressed && !dragging)
            {
                double dx = p.X - pressPt.X, dy = p.Y - pressPt.Y;
                if (dx * dx + dy * dy > DragThresholdPx * DragThresholdPx) dragging = true;
            }
        }

        public void MouseUp(double t, Point p)
        {
            if (!pressed) return;
            AddHist(t, p);
            if (dragging)
            {
                edgeReleased = true;
                releaseVel = Velocity(t);
                lastClickTime = -10;
            }
            else if (pressIsDouble)
            {
                edgeDouble = true;
                lastClickTime = -10;          // a third click starts a new sequence
            }
            else
            {
                edgeClicked = true;
                lastClickTime = t;
                lastClickPt = p;
            }
            pressed = false;
            dragging = false;
            pressIsDouble = false;
        }

        public void Cancel(double t, bool keepVelocity)
        {
            if (dragging)
            {
                edgeReleased = true;
                releaseVel = keepVelocity ? Velocity(t) : PointF.Empty;
            }
            pressed = false;
            dragging = false;
            pressIsDouble = false;
        }

        public void Tick(double t, Point cursor, bool overCat, bool nearCat, bool buttonDown, double petMinStep, double petLongPath)
        {
            AddHist(t, cursor);
            if (pressed)
            {
                if (!dragging)
                {
                    double dx = cursor.X - pressPt.X, dy = cursor.Y - pressPt.Y;
                    if (dx * dx + dy * dy > DragThresholdPx * DragThresholdPx) dragging = true;
                }
                if (!buttonDown) MouseUp(t, cursor);   // missed WM_LBUTTONUP, e.g. capture was stolen
            }

            if (overCat) lastOverCat = t;
            if (nearCat) lastNear = t;
            bool tracking = !pressed && !dragging && nearCat && (t - lastOverCat) < 0.4;
            if (!tracking && (t - lastNear) > 0.3) ClearPet();
            if (tracking) AddPet(t, cursor);
            if (tracking && petCount >= 3)
            {
                int rev;
                double path;
                Analyze(t, petMinStep, out rev, out path);
                if (rev >= 2 || path >= petLongPath) pettingUntil = t + PetHold;
            }
        }

        public void Fill(double t, CatInput input)
        {
            input.Clicked = edgeClicked;
            input.DoubleClicked = edgeDouble;
            input.DragActive = dragging;
            input.DragReleased = edgeReleased;
            input.ReleaseVelocity = edgeReleased ? releaseVel : PointF.Empty;
            input.Petting = !pressed && !dragging && t < pettingUntil;
            edgeClicked = edgeDouble = edgeReleased = false;
            releaseVel = PointF.Empty;
        }

        void AddHist(double t, Point p)
        {
            int last = (histHead - 1 + HistN) % HistN;
            if (histCount > 0 && histT[last] == t) { histX[last] = p.X; histY[last] = p.Y; return; }
            histT[histHead] = t; histX[histHead] = p.X; histY[histHead] = p.Y;
            histHead = (histHead + 1) % HistN;
            if (histCount < HistN) histCount++;
        }

        public PointF Velocity(double now)
        {
            if (histCount < 2) return PointF.Empty;
            int newest = (histHead - 1 + HistN) % HistN;
            int oldest = newest;
            for (int k = 1; k < histCount; k++)
            {
                int i = (newest - k + HistN) % HistN;
                if (now - histT[i] > VelocityWindow) { oldest = i; break; }
                oldest = i;
            }
            double dt = histT[newest] - histT[oldest];
            if (dt < 0.015) return PointF.Empty;
            // if the cursor has been still for a while before release, the throw is weak
            if (now - histT[newest] > VelocityWindow) return PointF.Empty;
            double vx = (histX[newest] - histX[oldest]) / dt;
            double vy = (histY[newest] - histY[oldest]) / dt;
            double sp = Math.Sqrt(vx * vx + vy * vy);
            if (sp > MaxReleaseSpeed) { vx *= MaxReleaseSpeed / sp; vy *= MaxReleaseSpeed / sp; }
            return new PointF((float)vx, (float)vy);
        }

        void ClearPet() { petCount = 0; petHead = 0; }

        void AddPet(double t, Point p)
        {
            int last = (petHead - 1 + PetN) % PetN;
            if (petCount > 0 && petX[last] == p.X && petY[last] == p.Y) { return; }
            petT[petHead] = t; petX[petHead] = p.X; petY[petHead] = p.Y;
            petHead = (petHead + 1) % PetN;
            if (petCount < PetN) petCount++;
        }

        // stroking = direction reversals on either axis, with minStep hysteresis against jitter
        void Analyze(double now, double minStep, out int reversals, out double path)
        {
            reversals = 0;
            path = 0;
            int n = petCount;
            int start = (petHead - n + PetN) % PetN;
            int first = -1;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % PetN;
                if (now - petT[i] <= PetWindow) { first = k; break; }
            }
            if (first < 0) return;
            int rx = 0, ry = 0;
            int dirX = 0, dirY = 0;
            double accX = 0, accY = 0;
            int pi = (start + first) % PetN;
            for (int k = first + 1; k < n; k++)
            {
                int i = (start + k) % PetN;
                int dx = petX[i] - petX[pi], dy = petY[i] - petY[pi];
                path += Math.Sqrt((double)dx * dx + (double)dy * dy);
                Step(dx, minStep, ref dirX, ref accX, ref rx);
                Step(dy, minStep, ref dirY, ref accY, ref ry);
                pi = i;
            }
            reversals = Math.Max(rx, ry);
        }

        static void Step(int d, double minStep, ref int dir, ref double acc, ref int rev)
        {
            if (d == 0) return;
            int s = d > 0 ? 1 : -1;
            if (dir == 0)
            {
                acc += Math.Abs(d);
                if (acc >= minStep) { dir = s; acc = 0; }
                return;
            }
            if (s == dir) { acc = 0; return; }
            acc += Math.Abs(d);
            if (acc >= minStep) { rev++; dir = s; acc = 0; }
        }
    }
}
