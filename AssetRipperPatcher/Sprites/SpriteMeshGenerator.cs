using System.Numerics;
using LibTessDotNet;

namespace AssetRipperPatcher.Sprites;

/// <summary>
/// <c>Runtime/Geometry/SpriteMeshGenerator.cpp</c> 的逐函数移植：
/// 从 alpha 掩码里描出轮廓 → 简化（Reduce/Embed 两种模式）→ 用 libtess2 三角化。
/// </summary>
/// <remarks>
/// 忠实移植，不改算法也不改数值：
/// <list type="bullet">
/// <item>所有浮点运算顺序、比较符号、<c>clamp</c> 边界都与原 C++ 一致。</item>
/// <item>原版用 <c>libtess2</c>；这里换成它的 C# 移植 <c>LibTessDotNet</c>（同为 MIT，同为 libtess2 算法）。
/// 原版调 <c>TESS_WINDING_NONZERO</c> + <c>TESS_POLYGONS</c> + polySize 3，对应
/// <see cref="WindingRule.NonZero"/> + <see cref="ElementType.Polygons"/> + 3。</item>
/// <item><c>path.m_invalid</c> 在原版里只做「标记失效顶点」，从未用于跳过计算，因此这里省略。</item>
/// </list>
/// </remarks>
internal sealed class SpriteMeshGenerator
{
    // Runtime/Geometry/SpriteMeshGenerator.cpp 顶部的常量
    private const float kHoleAreaLimit = 0.25F;
    private const float kMaxTriangles = 1000.0F;
    private const float kMaxOverdraw = 4.0F;
    private const float kResolution = 960 * 640 * kMaxOverdraw;

    // clip 边掩码（原版 #define LE/RE/BE/TE）
    private const int LE = 0x1;
    private const int RE = 0x2;
    private const int BE = 0x4;
    private const int TE = 0x8;

    /// <summary>原版 <c>struct s_cost</c>：简化代价。</summary>
    internal struct Cost
    {
        public float C;
        public float W;

        public Cost(float c, float w)
        {
            C = c;
            W = w;
        }
    }

    /// <summary>原版 <c>struct vertex</c>。用 class 是为了让 <c>select()</c> 能像 C++ 那样改邻居的 <c>p</c>/<c>c</c>。</summary>
    public sealed class PathVertex
    {
        public Vector2 P;
        public Vector2 N;   // normal
        public int S;       // sign -> indicating concavity
        public float C;
        public Cost VertexCost;

        public PathVertex(Vector2 pos)
        {
            P = pos;
        }
    }

    /// <summary>原版 <c>struct edge</c>：由两点确定的直线，带退化方向的符号约定。</summary>
    private readonly struct Edge
    {
        public readonly float A;
        public readonly float B;
        private readonly float m_c;
        private readonly bool m_apos;

        public Edge(Vector2 p0, Vector2 p1)
        {
            A = p0.Y - p1.Y;
            B = p1.X - p0.X;
            m_c = -p0.X * A - p0.Y * B;

            bool aez = A == 0;
            bool bez = B == 0;
            bool agz = A < 0;
            bool bgz = B < 0;
            m_apos = aez ? bgz : agz;
        }

        public float Grad(Vector2 p) => A * p.X + B * p.Y + m_c;

        public int Test(Vector2 p)
        {
            float g = Grad(p);
            return (g > 0) || ((g == 0) && m_apos) ? 1 : -1;
        }
    }

    /// <summary>原版 <c>path_segment</c>：Reduce 模式简化时优先队列里的分段。</summary>
    private sealed class PathSegment
    {
        public int I0;
        public int I1;
        public int Mx;
        public int Cnt;

        public PathSegment(List<PathVertex> path, int i0, int i1)
        {
            I0 = i0;
            I1 = i1;
            Mx = MaxDistance(path, i0, i1);
        }

        private int MaxDistance(List<PathVertex> path, int i0, int i1)
        {
            int n = path.Count;

            Vector2 a = path[i0].P;
            Vector2 b = path[i1].P;

            float dm = -1;
            int mi = -1;
            Cnt = 0;
            for (int i = i0; i != i1; i = Mod(++i, n), Cnt++)
            {
                float dq = DistancePointLine(path[i].P, a, b);
                if (dq < dm)
                {
                    continue;
                }
                dm = dq;
                mi = i;
            }
            return mi;
        }
    }

    /// <summary>原版 <c>class path</c>：一条闭合轮廓。</summary>
    public sealed class Path
    {
        public List<PathVertex> Vertices;
        public readonly bool IsHole;
        public Vector2 Min;
        public Vector2 Max;

        private readonly int m_bx;
        private readonly int m_by;
        private readonly int m_sign;
        private readonly float m_area;
        private readonly List<PathVertex> m_path0;
        // 原版 path::m_invalid：本轮需要重算代价的顶点下标，select() 会把被消顶点的邻居塞进来
        private readonly List<int> m_invalid = new();

        public Path(List<PathVertex> p, int w, int h, int sign, float area, float bias)
        {
            m_bx = w;
            m_by = h;
            m_area = area;
            m_sign = sign;
            IsHole = sign == '-';
            Vertices = p;
            Opt(bias);
            Bbox();
            // m_path0 = m_path（C++ 的 vector 赋值是逐元素拷贝，含 n/s/c/cost）
            m_path0 = CloneVertices(Vertices);
        }

        private static List<PathVertex> CloneVertices(List<PathVertex> source)
        {
            List<PathVertex> copy = new(source.Count);
            foreach (PathVertex v in source)
            {
                copy.Add(new PathVertex(v.P)
                {
                    N = v.N,
                    S = v.S,
                    C = v.C,
                    VertexCost = v.VertexCost,
                });
            }
            return copy;
        }

        public Vector2 GetMin() => Min;

        public Vector2 GetMax() => Max;

        /// <summary>原版 <c>path::bbox</c>，含 clamp 到 [0, w] / [0, h]。</summary>
        private void Bbox()
        {
            float minx = float.MaxValue;
            float miny = float.MaxValue;
            float maxx = SpriteMath.FloatMin;
            float maxy = SpriteMath.FloatMin;

            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = Vertices[i].P;
                if (p.X < minx) minx = p.X;
                if (p.Y < miny) miny = p.Y;
                if (p.X > maxx) maxx = p.X;
                if (p.Y > maxy) maxy = p.Y;
            }

            minx = minx < 0.0F ? 0.0F : minx > m_bx ? m_bx : minx;
            miny = miny < 0.0F ? 0.0F : miny > m_by ? m_by : miny;
            maxx = maxx < 0.0F ? 0.0F : maxx > m_bx ? m_bx : maxx;
            maxy = maxy < 0.0F ? 0.0F : maxy > m_by ? m_by : maxy;

            Min = new Vector2(minx, miny);
            Max = new Vector2(maxx, maxy);
        }

        /// <summary>原版 <c>path::dec</c>：算顶点法线（相邻两条边法线的角平分线）。</summary>
        public bool Dec(int i)
        {
            int n = Vertices.Count;
            if (n < 3)
            {
                return false;
            }

            Vector2 a = Vertices[Mod(i - 1, n)].P;
            Vector2 b = Vertices[Mod(i + 0, n)].P;
            Vector2 c = Vertices[Mod(i + 1, n)].P;
            Vector2 ab = a - b;
            Vector2 bc = b - c;
            Vector2 na = SpriteMath.NormalizeSafe(new Vector2(-ab.Y, ab.X));
            Vector2 nb = SpriteMath.NormalizeSafe(new Vector2(-bc.Y, bc.X));
            Vector2 no = SpriteMath.NormalizeSafe(nb + na);
            Vertices[Mod(i, n)].N = no;
            return true;
        }

        /// <summary>原版 <c>path::inf</c>：标记凹凸性（凹 = -1 / 凸 = 1）。</summary>
        public bool Inf(int i)
        {
            int n = Vertices.Count;
            if (n < 3)
            {
                return false;
            }
            Vector2 a = Vertices[Mod(i - 1, n)].P;
            Vector2 b = Vertices[Mod(i + 0, n)].P;
            Vector2 c = Vertices[Mod(i + 1, n)].P;
            Vertices[i].S = new Edge(a, c).Test(b);
            return true;
        }

        private bool ClipTest(Vector2 p, int side)
        {
            return side switch
            {
                LE => p.X >= Min.X,
                RE => p.X <= Max.X,
                TE => p.Y >= Min.Y,
                BE => p.Y <= Max.Y,
                _ => false,
            };
        }

        private Vector2 ClipIsec(Vector2 p, Vector2 q, int e)
        {
            double a = (q.Y - p.Y) / (q.X - p.X);
            double b = p.Y - p.X * a;
            double x = 0, y = 0;
            switch (e)
            {
                case LE:
                case RE:
                    x = e == LE ? Min.X : Max.X;
                    y = x * a + b;
                    break;
                case TE:
                case BE:
                    y = e == TE ? Min.Y : Max.Y;
                    // 原版判 IsFinite(a)；.NET 里 NaN/Inf 都能被 double.IsFinite 覆盖
                    x = double.IsFinite(a) ? (y - b) / a : p.X;
                    break;
            }
            return new Vector2((float)x, (float)y);
        }

        private void ClipEdge(int e)
        {
            int n = Vertices.Count;
            List<PathVertex> cpath = new();

            for (int i = 0; i < n; i++)
            {
                Vector2 s = Vertices[Mod(i + 0, n)].P;
                Vector2 p = Vertices[Mod(i + 1, n)].P;
                if (ClipTest(p, e))
                {
                    if (!ClipTest(s, e))
                    {
                        cpath.Add(new PathVertex(ClipIsec(p, s, e)));
                    }
                    cpath.Add(new PathVertex(p));
                }
                else if (ClipTest(s, e))
                {
                    cpath.Add(new PathVertex(ClipIsec(s, p, e)));
                }
            }
            Vertices = cpath;
        }

        /// <summary>原版 <c>path::clip</c>：把轮廓夹回边界框。</summary>
        private void Clip()
        {
            ClipEdge(LE);
            ClipEdge(RE);
            ClipEdge(TE);
            ClipEdge(BE);
        }

        /// <summary>原版 <c>path::self_intersect</c>。</summary>
        public int SelfIntersect(Vector2 p0, Vector2 p1)
        {
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p2 = Vertices[Mod(i + 0, n)].P;
                Vector2 p3 = Vertices[Mod(i + 1, n)].P;
                if (p2 == p0 || p3 == p1 || p1 == p2 || p0 == p3)
                {
                    continue;
                }
                if (LSegIntersect(p0, p1, p2, p3))
                {
                    return 1;
                }
            }
            return 0;
        }

        /// <summary>原版 <c>path::cvx_cost</c>：凸顶点的简化代价。</summary>
        public bool CvxCost(int i)
        {
            int n = Vertices.Count;
            if (n < 5)
            {
                return false;
            }

            PathVertex v = Vertices[i];
            Vector2 sn = Vertices[Mod(i - 1, n)].N;
            Vector2 tn = Vertices[Mod(i + 1, n)].N;
            // detect high convexity -> better triangulation for cyclic geometric shape
            float q = Vector2.Dot(sn, tn);
            if (q < 0.000F || SpriteMath.CompareFloatRobust(q, 0.0F) || SpriteMath.CompareFloatRobust(q, 1.0F))
            {
                v.VertexCost = new Cost(-1, 0.0F);
                return true;
            }

            Vector2 s0 = Vertices[Mod(i - 1, n)].P;
            Vector2 t0 = Vertices[Mod(i + 1, n)].P;
            Vector2 p0 = v.P;
            Vector2 a0 = Ortho(sn);
            Vector2 c0 = Ortho(p0 - s0);
            Vector2 c1 = s0 - t0;
            Vector2 b0 = tn - sn;
            float c = Vector2.Dot(c0, c1);
            float a = Vector2.Dot(tn, a0);
            float b = Vector2.Dot(b0, c0) + Vector2.Dot(c1, a0);

            float x0 = -1, x1 = -1, w = 0;
            if (SpriteMath.QuadraticPolynomialRootsGeneric(a, -b, c, ref x0, ref x1) != 0
                && MinPositive(x0, x1, ref w))
            {
                Vector2 r0 = Vertices[Mod(i - 2, n)].P;
                Vector2 u0 = Vertices[Mod(i + 2, n)].P;
                Vector2 s1 = s0 + sn * w;
                Vector2 t1 = t0 + tn * w;

                float d0 = Det(r0, s1, s0);
                float d1 = Det(s1, p0, s0);
                float d2 = Det(p0, t1, t0);
                float d3 = Det(t1, u0, t0);
                float d = d0 + d1 + d2 + d3;
                v.VertexCost = new Cost(d, w);
                return true;
            }
            else
            {
                // this should not ever happen.. but handle it anyway
                v.VertexCost = new Cost(-1.0F, 0.0F);
                return false;
            }
        }

        /// <summary>原版 <c>path::cve_cost</c>：凹顶点的简化代价。</summary>
        public bool CveCost(int i)
        {
            int n = Vertices.Count;
            if (n < 3)
            {
                return false;
            }
            PathVertex v = Vertices[i];
            Vector2 s = Vertices[Mod(i - 1, n)].P;
            Vector2 t = Vertices[Mod(i + 1, n)].P;
            Vector2 u = v.P;
            float d = Det(s, t, u);
            if (d > 0 || SpriteMath.CompareFloatRobust(d, 0.0F))
            {
                v.VertexCost = new Cost(d, 0.0F);
                return true;
            }
            else
            {
                v.VertexCost = new Cost(-1.0F, 0.0F);
                return false;
            }
        }

        private int MinCost()
        {
            int n = Vertices.Count;
            int min_i = -1;
            float min_c = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                PathVertex v = Vertices[i];
                if (v.VertexCost.C < 0)
                {
                    continue;
                }
                float c = v.VertexCost.C + v.C;
                if (c < min_c)
                {
                    min_c = c;
                    min_i = i;
                }
            }
            return min_i;
        }

        /// <summary>原版 <c>path::select</c>：消掉代价最小的顶点。</summary>
        private bool Select()
        {
            int n = Vertices.Count;
            if (n < 5)
            {
                return false;
            }

            int i;
            int m = 0;
            bool found = false;
            do
            {
                i = MinCost();
                if (i < 0)
                {
                    return false;
                }
                PathVertex v = Vertices[i];
                PathVertex s = Vertices[Mod(i - 1, n)];
                PathVertex t = Vertices[Mod(i + 1, n)];
                Cost cost = v.VertexCost;
                int isec;
                if (v.S > 0)
                {
                    Vector2 s0 = s.P;
                    Vector2 t0 = t.P;
                    // check self intersection
                    isec = SelfIntersect(s0, t0);

                    if (isec != 0)
                    {
                        v.VertexCost.C = -1;
                    }
                    else
                    {
                        s.C += cost.C;
                        t.C += cost.C;
                        m_invalid.Add(Mod(i + 0, n));
                        m_invalid.Add(Mod(i + 1, n));
                        found = true;
                    }
                }
                else
                {
                    PathVertex r = Vertices[Mod(i - 2, n)];
                    PathVertex u = Vertices[Mod(i + 2, n)];
                    Vector2 r0 = r.P;
                    Vector2 u0 = u.P;
                    Vector2 s0 = s.P + s.N * cost.W;
                    Vector2 t0 = t.P + t.N * cost.W;
                    // check self intersection
                    isec = SelfIntersect(r0, s0);
                    isec |= SelfIntersect(s0, t0);
                    isec |= SelfIntersect(t0, u0);

                    if (isec != 0)
                    {
                        v.VertexCost.C = -1;
                    }
                    else
                    {
                        s.P = s0;
                        t.P = t0;
                        s.C += cost.C;
                        t.C += cost.C;
                        m_invalid.Add(Mod(i - 2, n));
                        m_invalid.Add(Mod(i - 1, n));
                        m_invalid.Add(Mod(i + 1, n));
                        m_invalid.Add(Mod(i + 2, n));
                        found = true;
                    }
                }
            }
            while (m++ < n && !found);

            if (found)
            {
                Vertices.RemoveAt(i);
                // fix invalid indices after erase
                m = m_invalid.Count;
                for (int k = 0; k < m; k++)
                {
                    if (m_invalid[k] > i)
                    {
                        m_invalid[k] -= 1;
                    }
                }
            }
            return found;
        }

        private int FindMaxDistance(int i0)
        {
            int n = Vertices.Count;

            Vector2 a = Vertices[i0].P;
            float dm = -1;
            int mi = -1;

            for (int i = 0; i < n; i++)
            {
                Vector2 b = Vertices[Mod(i, n)].P;
                float ba = SpriteMath.Magnitude(b - a);
                if (ba < dm)
                {
                    continue;
                }
                dm = ba;
                mi = i;
            }
            return mi;
        }

        /// <summary>原版 <c>path::simplify</c>：mode 1 走 Embed（贪心消点），否则走 Reduce（优先队列抽稀）。</summary>
        public void Simplify(float q, int mode)
        {
            Vertices = CloneVertices(m_path0);
            m_invalid.Clear();

            int n = Vertices.Count;
            int lim = (int)(n * (1.0F - SpriteMath.Clamp01(q)));

            if (n < 5)
            {
                goto bail_out;
            }

            if (mode == (int)PathSimplifyMode.kPathEmbed)
            {
                if (lim < 5)
                {
                    lim = 5;
                }

                // mark all vertices invalid
                for (int i = 0; i < n; i++)
                {
                    m_invalid.Add(i);
                }

                do
                {
                    n = Vertices.Count;
                    int m = m_invalid.Count;
                    for (int i = 0; i < m; i++)
                    {
                        Dec(m_invalid[i]);
                        Inf(m_invalid[i]);
                    }
                    for (int i = 0; i < m; i++)
                    {
                        int k = m_invalid[i];
                        if (Vertices[k].S > 0)
                        {
                            CveCost(k);
                        }
                        else
                        {
                            CvxCost(k);
                        }
                    }
                    m_invalid.Clear();
                    if (!Select())
                    {
                        break;
                    }
                }
                while (n > lim);
            }
            else
            {
                if (lim < 4)
                {
                    lim = 4;
                }

                int i0 = FindMaxDistance(0);
                int i1 = FindMaxDistance(i0);

                PathSegment ls = new(Vertices, i0, i1);
                PathSegment rs = new(Vertices, i1, i0);

                // std::priority_queue + compare_path_segment(s0.m_cnt < s1.m_cnt) 取的是 m_cnt 最大的段，
                // .NET 的 PriorityQueue 取最小优先级，所以入队用 -Cnt。
                PriorityQueue<PathSegment, int> pq = new();
                if (ls.Mx >= 0)
                {
                    pq.Enqueue(ls, -ls.Cnt);
                }
                if (rs.Mx >= 0)
                {
                    pq.Enqueue(rs, -rs.Cnt);
                }

                bool[] select = new bool[n];
                select[i0] = true;
                select[i1] = true;

                int count = 2;
                while (pq.Count > 0)
                {
                    PathSegment ts = pq.Dequeue();

                    select[ts.Mx] = true;
                    if (++count == lim)
                    {
                        break;
                    }
                    // split
                    ls = new PathSegment(Vertices, ts.I0, ts.Mx);
                    if (ls.Mx >= 0)
                    {
                        pq.Enqueue(ls, -ls.Cnt);
                    }
                    rs = new PathSegment(Vertices, ts.Mx, ts.I1);
                    if (rs.Mx >= 0)
                    {
                        pq.Enqueue(rs, -rs.Cnt);
                    }
                }

                List<PathVertex> kept = new(n);
                for (int i = 0; i < n; i++)
                {
                    if (select[i])
                    {
                        kept.Add(Vertices[i]);
                    }
                }
                Vertices = kept;
            }

        bail_out:
            if (m_sign == '+' && mode == (int)PathSimplifyMode.kPathEmbed)
            {
                Clip();
            }
        }

        /// <summary>原版 <c>path::fit</c>：把 [i0, i1] 段折线拟合为直线。</summary>
        private void Fit(List<int> ci, int i0, int i1)
        {
            int n = Vertices.Count;

            if (Mod(i0 + 1, n) == i1 || i0 == i1)
            {
                ci.Add(i1);
                return;
            }

            Vector2 a = Vertices[i0].P;
            Vector2 b = Vertices[i1].P;
            Edge e = new(a, b);
            int im = -1;
            float qm = -1;
            int ic = i0;
            do
            {
                float qc = MathF.Abs(e.Grad(Vertices[ic].P));
                if (qc > qm)
                {
                    im = ic;
                    qm = qc;
                }
                if (ic == i1)
                {
                    break;
                }
                ic = Mod(ic + 1, n);
            }
            while (true);

            float lim = MathF.Max(MathF.Abs(e.A) * 0.5F, MathF.Abs(e.B) * 0.5F);
            if (qm <= lim || im < 0)
            {
                ci.Add(i1);
                return;
            }
            Fit(ci, i0, im);
            Fit(ci, im, i1);
        }

        /// <summary>原版 <c>path::opt</c>：按主方向把轮廓切成若干直线段并拟合。</summary>
        private bool Opt(float bias)
        {
            int n = Vertices.Count;
            if (n < 3)
            {
                return false;
            }

            List<int> cp = new();
            List<int> ci = new();

            int s = -1;
            int[] dt = new int[8];
            cp.Add(0);
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = Vertices[i].P;
                Vector2 p1 = Vertices[Mod(i + 1, n)].P;

                int d = Dir(p0, p1);
                if (d < 0)
                {
                    continue;
                }
                dt[d] = 1;
                if (s < 0)
                {
                    s = d;
                    continue;
                }
                // cut path, if direction change is not possible for straight line
                if (!(d == Mod(s - 1, 8) || d == Mod(s + 1, 8) || d == s)
                    || dt[0] + dt[1] + dt[2] + dt[3] + dt[4] + dt[5] + dt[6] + dt[7] > 2)
                {
                    Array.Clear(dt);
                    s = -1;
                    cp.Add(i);
                }
            }

            // fit sub paths to straight line
            int m = cp.Count;
            for (int i = 0; i < m; i++)
            {
                Fit(ci, cp[i], cp[Mod(i + 1, m)]);
            }

            // rm extra vertices
            List<PathVertex> tmp = Vertices;
            Vertices = new List<PathVertex>(ci.Count);
            for (int i = 0; i < ci.Count; i++)
            {
                Vertices.Add(new PathVertex(tmp[ci[i]].P));
            }

            // unit normals
            n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Dec(i);
            }

            // bias
            for (int i = 0; i < n; i++)
            {
                Vertices[i].P += Vertices[i].N * bias;
            }

            return true;
        }
    }

    private readonly List<Path> m_paths = new();
    private Mask m_mask_org;
    private Mask m_mask_cur;

    public IReadOnlyList<Path> GetPaths() => m_paths;

    /// <summary>原版 <c>SpriteMeshGenerator::MakeShape</c>。</summary>
    public void MakeShape(
        ReadOnlySpan<byte> rgba,
        int imageWidth,
        int imageHeight,
        float hullTolerance,
        byte alphaTolerance,
        bool holeDetection,
        uint extrude,
        float bias,
        int mode)
    {
        m_mask_org = new Mask(rgba, imageWidth, imageHeight, alphaTolerance, extrude);
        m_mask_cur = new Mask(rgba, imageWidth, imageHeight, alphaTolerance, extrude);

        float areaRect = imageWidth * imageHeight;
        float areaTotal = 0;
        while (Contour(out List<PathVertex> outline, out int sign, out float area))
        {
            if (!holeDetection && sign == '-')
            {
                continue;
            }
            if (area < areaRect * kHoleAreaLimit && sign == '-' && hullTolerance < 0.0F)
            {
                continue;
            }
            areaTotal += sign == '+' ? area : -area;
            m_paths.Add(new Path(outline, imageWidth, imageHeight, sign, area, bias));
        }

        if (hullTolerance < 0.0F)
        {
            hullTolerance = EvaluateLOD(kMaxTriangles / kResolution, areaTotal);
        }
        // Simplify
        foreach (Path p in m_paths)
        {
            p.Simplify(hullTolerance, mode);
        }
        // Snap
        foreach (Path p in m_paths)
        {
            foreach (PathVertex vert in p.Vertices)
            {
                vert.P.X = SpriteMath.Roundf(vert.P.X);
                vert.P.Y = SpriteMath.Roundf(vert.P.Y);
            }
        }
    }

    /// <summary>原版 <c>SpriteMeshGenerator::evaluateLOD</c>。</summary>
    private float EvaluateLOD(float areaHint, float area)
    {
        // do rough estimation of simplification lod
        int triangleCount = 0;
        int n = m_paths.Count;

        // evaluate optimal triangle count
        for (int i = 0; i < n; i++)
        {
            Path p = m_paths[i];
            if (p.IsHole)
            {
                triangleCount += 2;
            }
            else
            {
                triangleCount += p.Vertices.Count - 2;
            }
        }
        float maxTriangleCount = area * areaHint;
        if (triangleCount == 0)
        {
            // 原版此处会除以 0 得到 NaN；C# 里 float 除以 0 同样是 Inf，clamp 后落到 0。
            return 0.0F;
        }
        float lod = 1.0F - maxTriangleCount / triangleCount;
        return SpriteMath.Clamp(lod, 0.0F, 1.0F);
    }

    /// <summary>原版 <c>SpriteMeshGenerator::FindBounds</c>：返回是否覆盖了 bounds。</summary>
    public bool FindBounds(out Rectf bounds)
    {
        if (m_paths.Count == 0)
        {
            bounds = default;
            return false;
        }

        Path minX = m_paths.MinBy(p => p.GetMin().X)!;
        Path minY = m_paths.MinBy(p => p.GetMin().Y)!;
        Path maxX = m_paths.MaxBy(p => p.GetMax().X)!;
        Path maxY = m_paths.MaxBy(p => p.GetMax().Y)!;

        bounds = new Rectf(minX.GetMin().X, minY.GetMin().Y, 0, 0);
        bounds.SetRight(maxX.GetMax().X);
        bounds.SetBottom(maxY.GetMax().Y);
        return true;
    }

    /// <summary>
    /// 原版 <c>SpriteMeshGenerator::Decompose</c>：把所有轮廓丢给 tessellator 生成三角形。
    /// 顶点按「坐标相同即复用」去重（容差 = <c>Vector2f::epsilon</c>），并且会 snap 到整数格点。
    /// </summary>
    public void Decompose(List<Vector2> vertices, List<int> indices)
    {
        if (m_paths.Count == 0)
        {
            return;
        }

        vertices.Clear();
        indices.Clear();

        const int kPolygonVertices = 3;

        Tess tess = new();
        foreach (Path p in m_paths)
        {
            List<PathVertex> pathVertices = p.Vertices;
            if (pathVertices.Count == 0)
            {
                continue;
            }

            ContourVertex[] contour = new ContourVertex[pathVertices.Count];
            for (int i = 0; i < contour.Length; i++)
            {
                Vector2 pos = pathVertices[i].P;
                contour[i] = new ContourVertex { Position = new Vec3(pos.X, pos.Y, 0) };
            }
            // forceOrientation 传 Original：与原版 tessAddContour 不强制方向一致，
            // 绕数由 Tessellate 的 windingRule 决定。
            tess.AddContour(contour, ContourOrientation.Original);
        }

        try
        {
            tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, kPolygonVertices);
        }
        catch (InvalidOperationException)
        {
            // 退化输入（全共线等）在原版里是 AssertBreak，Release 下会继续用空结果。
            return;
        }

        int elemCount = tess.ElementCount;
        int[] elements = tess.Elements;
        ContourVertex[] verts = tess.Vertices;

        for (int e = 0; e < elemCount; e++)
        {
            int baseIndex = e * kPolygonVertices;

            // Extract vertices
            for (int i = 0; i < kPolygonVertices; i++)
            {
                int index = elements[baseIndex + i];
                if (index < 0)
                {
                    // TESS_UNDEF：原版断言后仍会读越界，这里直接跳过该三角形
                    continue;
                }

                float x = verts[index].Position.X;
                float y = verts[index].Position.Y;

                // SNAP_VERTEX_POSITION
                x = MathF.Floor(x + 0.5F);
                y = MathF.Floor(y + 0.5F);

                Vector2 newVertex = new(x, y);

                // Reuse vertex
                bool reused = false;
                for (int v = 0; v < vertices.Count; v++)
                {
                    Vector2 vertex = vertices[v];
                    if (MathF.Abs(vertex.X - x) <= SpriteMath.VectorEpsilon
                        && MathF.Abs(vertex.Y - y) <= SpriteMath.VectorEpsilon)
                    {
                        indices.Add(v);
                        reused = true;
                        break;
                    }
                }

                // New vertex
                if (!reused)
                {
                    indices.Add(vertices.Count);
                    vertices.Add(newVertex);
                }
            }
        }
    }

    /// <summary>原版文件级 <c>mod(a, n)</c>：C++ 的取模（负数向正方向回绕）。</summary>
    private static int Mod(int a, int n)
    {
        return a >= n ? a % n : a >= 0 ? a : n - 1 - (-1 - a) % n;
    }

    /// <summary>原版文件级 <c>det</c>。</summary>
    private static float Det(Vector2 a, Vector2 b, Vector2 c)
    {
        float bax = b.X - a.X;
        float acx = a.X - c.X;
        float aby = a.Y - b.Y;
        float cay = c.Y - a.Y;
        return bax * cay - acx * aby;
    }

    /// <summary>原版文件级 <c>ortho</c>。</summary>
    private static Vector2 Ortho(Vector2 v) => new(v.Y, -v.X);

    /// <summary>原版文件级 <c>distance_point_line</c>。</summary>
    private static float DistancePointLine(Vector2 pq, Vector2 p0, Vector2 p1)
    {
        Vector2 v = p1 - p0;
        Vector2 w = pq - p0;

        float a = Vector2.Dot(w, v);
        if (a <= 0)
        {
            return SpriteMath.Magnitude(p0 - pq);
        }

        float b = Vector2.Dot(v, v);
        if (b <= a)
        {
            return SpriteMath.Magnitude(p1 - pq);
        }

        float c = a / b;
        Vector2 p = p0 + v * c;
        return SpriteMath.Magnitude(p - pq);
    }

    /// <summary>原版文件级 <c>dir</c>：把方向量化为八方向编号（-1 表示无效）。</summary>
    private static int Dir(Vector2 p0, Vector2 p1)
    {
        int[] di = [0, 1, 2, 7, -1, 3, 6, 5, 4];
        Vector2 dt = p0 - p1;
        int dx = dt.X > 0.0F ? 1 : dt.X < 0.0F ? -1 : 0;
        int dy = dt.Y > 0.0F ? 1 : dt.Y < 0.0F ? -1 : 0;
        int d = 4 + 3 * dx - dy;
        // 原版写的是 (d >= 0 || d <= 8)，恒真；保留原样语义，越界由索引检查兜住
        return d >= 0 && d <= 8 ? di[d] : -1;
    }

    /// <summary>原版文件级 <c>min_positive</c>。</summary>
    private static bool MinPositive(float a, float b, ref float res)
    {
        if (a > 0 && b > 0)
        {
            res = a < b ? a : b;
        }
        else
        {
            res = a > b ? a : b;
        }
        return res > 0 || SpriteMath.CompareFloatRobust(res, 0.0F);
    }

    /// <summary>原版文件级 <c>lseg_intersect</c>。</summary>
    private static bool LSegIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        Vector2 e43 = p4 - p3;
        Vector2 e21 = p2 - p1;
        Vector2 e13 = p1 - p3;

        double dn = (double)e43.Y * e21.X - (double)e43.X * e21.Y;
        double na = (double)e43.X * e13.Y - (double)e43.Y * e13.X;
        double nb = (double)e21.X * e13.Y - (double)e21.Y * e13.X;

        // coincident?
        if (Math.Abs(na) < SpriteMath.VectorEpsilon
            && Math.Abs(nb) < SpriteMath.VectorEpsilon
            && Math.Abs(dn) < SpriteMath.VectorEpsilon)
        {
            return false;
        }

        // parallel ?
        if (Math.Abs(dn) < SpriteMath.VectorEpsilon)
        {
            return false;
        }

        // collinear ?
        double mua = na / dn;
        double mub = nb / dn;
        if (mua < 0 || mua > 1 || mub < 0 || mub > 1)
        {
            return false;
        }

        return true;
    }

    /// <summary>原版 <c>SpriteMeshGenerator::invmask</c>：把描出的轮廓从掩码里抹掉。</summary>
    private bool InvMask(List<PathVertex> outline)
    {
        int n = outline.Count;
        if (n <= 0)
        {
            return false;
        }

        int xa = (int)outline[0].P.X;
        Vector2 pp = outline[n - 1].P;

        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = outline[i].P;

            while (i + 1 < n && p0.Y == outline[i + 1].P.Y)
            {
                int d = Dir(p0, outline[i + 1].P);
                if ((d == 1 && pp.Y < p0.Y) || (d == 5 && pp.Y > p0.Y))
                {
                    p0 = outline[i + 1].P;
                }
                i++;
            }
            int y = (int)p0.Y;
            int x0 = Math.Min(xa, (int)p0.X);
            int x1 = Math.Max(xa, (int)p0.X);
            for (int x = x0; x < x1; x++)
            {
                m_mask_cur.Inv(x, y);
            }

            if (i + 1 < n && pp.Y != p0.Y && outline[i + 1].P.Y == pp.Y)
            {
                y = (int)p0.Y;
                x0 = Math.Min(xa, (int)p0.X);
                x1 = Math.Max(xa, (int)p0.X);
                for (int x = x0; x < x1; x++)
                {
                    m_mask_cur.Inv(x, y);
                }
            }
            pp = p0;
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 p = outline[i].P;
            m_mask_cur.Rst((int)p.X, (int)p.Y);
        }
        return true;
    }

    /// <summary>原版 <c>SpriteMeshGenerator::trace</c>：沿掩码边界找下一个轮廓点。</summary>
    private bool Trace(Vector2 p0, Vector2 p1, out Vector2 p)
    {
        int[,] dt =
        {
            { -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 },
            { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 },
        };

        int t0 = Dir(p0, p1);
        if (t0 < 0)
        {
            p = new Vector2(-1, -1);
            return false;
        }

        for (int i = 0; i < 8; i++)
        {
            int t = (t0 + i) % 8;
            int x = (int)p1.X + dt[t, 0];
            int y = (int)p1.Y + dt[t, 1];
            if (m_mask_cur.Tst(x, y))
            {
                p = new Vector2(x, y);
                return true;
            }
        }

        p = new Vector2(-1, -1);
        return false;
    }

    /// <summary>原版 <c>SpriteMeshGenerator::contour</c>：取下一段闭合轮廓。</summary>
    private bool Contour(out List<PathVertex> outline, out int sign, out float area)
    {
        outline = new List<PathVertex>();
        area = 0.0F;
        sign = '+';
        do
        {
            outline.Clear();
            int b = m_mask_cur.First();
            if (b < 0)
            {
                return false;
            }

            int x = b % m_mask_cur.W;
            int y = b / m_mask_cur.W;
            Vector2 curr = new(x, y);
            Vector2 stop;
            Vector2 prev;
            Vector2 next;
            area = 0.0F;
            sign = m_mask_org.Tst(x, y) ? '+' : '-';
            stop = curr;
            next = curr;
            curr.X -= 1;

            do
            {
                prev = curr;
                curr = next;
                outline.Add(new PathVertex(curr));
                if (!Trace(prev, curr, out next))
                {
                    break;
                }

                area += curr.X * next.Y - curr.Y * next.X;
                if (next == stop)
                {
                    break;
                }
            }
            while (true);

            InvMask(outline);
            if (MathF.Abs(area) < 4)
            {
                area = 0;
                continue;
            }
            if ((sign == '+' && area < 0) || (sign == '-' && area > 0))
            {
                outline.Reverse();
            }
            area = MathF.Abs(area);
            break;
        }
        while (true);
        return true;
    }

    /// <summary>原版 <c>SpriteMeshGenerator::mask</c>：alpha 阈值化的位图 + 腐蚀扩张。</summary>
    private sealed class Mask
    {
        public readonly int W;
        public readonly int H;
        private readonly bool[] m_bv;

        public Mask(ReadOnlySpan<byte> rgba, int width, int height, byte acut, uint dn)
        {
            int w = width;
            int h = height;
            int n = w * h;
            bool[] bv = new bool[n];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // ColorRGBA32 是 RGBA 字节序，alpha 在第 4 字节
                    if (rgba[(x + y * width) * 4 + 3] > acut)
                    {
                        bv[x + y * w] = true;
                    }
                }
            }

            if (dn != 0)
            {
                Dilate((int)dn, w, h, bv);
            }

            W = w + 1;
            H = h + 1;
            m_bv = new bool[W * H];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (bv[x + y * width])
                    {
                        m_bv[x + 0 + (y + 0) * W] = true;
                        m_bv[x + 1 + (y + 1) * W] = true;
                        m_bv[x + 0 + (y + 1) * W] = true;
                        m_bv[x + 1 + (y + 0) * W] = true;
                    }
                }
            }
        }

        private Mask(int w, int h)
        {
            W = w;
            H = h;
            m_bv = new bool[w * h];
        }

        public bool Tst(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
            {
                return false;
            }
            return m_bv[x + y * W];
        }

        public void Set(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
            {
                return;
            }
            m_bv[x + y * W] = true;
        }

        public void Rst(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
            {
                return;
            }
            m_bv[x + y * W] = false;
        }

        public void Inv(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
            {
                return;
            }
            m_bv[x + y * W] = !m_bv[x + y * W];
        }

        public int First()
        {
            int n = m_bv.Length;
            for (int i = 0; i < n; i++)
            {
                if (m_bv[i])
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>原版 <c>mask::dilate</c>：按曼哈顿距离膨胀 n 圈（两趟扫描求距离场）。</summary>
        private void Dilate(int n, int w, int h, bool[] bv)
        {
            if (w == 0 || h == 0)
            {
                return;
            }

            int[] md = new int[w * h];
            // 原版 mdist：正向 + 反向两趟，距离用 int 存
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    if (bv[i])
                    {
                        md[i] = 0;
                    }
                    else
                    {
                        md[i] = w + h;
                        if (y > 0) md[i] = Math.Min(md[i], md[i - w] + 1);
                        if (x > 0) md[i] = Math.Min(md[i], md[i - 1] + 1);
                    }
                }
            }

            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = x + y * w;
                    if (y + 1 < h) md[i] = Math.Min(md[i], md[i + w] + 1);
                    if (x + 1 < w) md[i] = Math.Min(md[i], md[i + 1] + 1);
                }
            }

            for (int i = 0; i < w * h; i++)
            {
                if (md[i] <= n)
                {
                    bv[i] = true;
                }
            }
        }
    }
}
