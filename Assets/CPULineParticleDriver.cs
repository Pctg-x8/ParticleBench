using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

interface IParticleDriver
{
    void Spawn(Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f);
    void Spawn(int count, Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f);
}

public sealed class CPULineParticleDriver : MonoBehaviour, IParticleDriver
{
    public static CPULineParticleDriver Self { get; private set; }
    void Awake() { Self = this; }

    /// GPU Compatible SpawnData Structure
    public struct SpawnData
    {
        public Vector3 forward; public float colorSamplingV;
        public Vector4 position;
        public float initialSpeedFactor, reductionRate, speedMag, lengthMag;
    }

    private const int MaxInstanceBlocks = 2048;
    private static readonly int ColorGradientTextureID = Shader.PropertyToID("_ColorGradient");
    [SerializeField] private Material _renderer;
    private int gradientTextureHeight;

    void OnEnable()
    {
        this.gradientTextureHeight = this._renderer.GetTexture(ColorGradientTextureID).height;
        this.SetupFreelist();
    }
    void OnDisable() { this.freeList.Clear(); }

    private static Vector3 ForwardVec(float th) => new Vector3(Mathf.Sin(th), Mathf.Cos(th), 0.0f);
    private static float ColorSamplingV(int gindex) => (gindex + 0.5f) / Self.gradientTextureHeight;
    public void Spawn(Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f)
    {
        var newIndex = this.freeList.Extract();
        if(newIndex >= this.maxInstanceIndex) this.maxInstanceIndex++;
        this.instanceStore[newIndex] = new InstanceData
        {
            forward = ForwardVec(Random.Range(0.0f, Mathf.PI * 2.0f)), position = new Vector4(at.x, at.y, at.z, 1.0f),
            colorSamplingV = ColorSamplingV(gradientIndex), initialSpeedFactor = Random.Range(0.25f, 1.5f),
            reductionRate = reductionRate, speedMag = speedMag, lengthMag = lengthMag, spawnTime = Time.time
        };
    }
    public void Spawn(int count, Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f)
    {
        while(count > 0) { Spawn(at, gradientIndex, speedMag, lengthMag, reductionRate); count--; }
    }

    void OnPreRender() { this.UpdateAllParticles(); }
    void OnPostRender() { this.DrawParticles(); }

    // COMPUTING //
    class InstanceData
    {
        public Vector4 position, forward;
        public float initialSpeedFactor, reductionRate, speedMag, lengthMag, spawnTime, colorSamplingV;
    };
    struct InstanceBlock
    {
        public Vector4[] pos1Array, pos2Array, colorSampleCoordArray;
    }
    private InstanceData[] instanceStore = new InstanceData[MaxInstanceBlocks * 1024];
    private BinaryHeap<int> freeList = new BinaryHeap<int>();
    private static readonly int _Pos1 = Shader.PropertyToID("_pos1");
    private static readonly int _Pos2 = Shader.PropertyToID("_pos2");
    private static readonly int _ColorSampleCoord = Shader.PropertyToID("_colorSampleCoord");
    // private InstanceBlock[] drawInstances = new InstanceBlock[MaxInstanceBlocks];
    private InstanceBlock drawInstance = new InstanceBlock
    {
        pos1Array = new Vector4[MaxInstanceBlocks * 1024], pos2Array = new Vector4[MaxInstanceBlocks * 1024],
        colorSampleCoordArray = new Vector4[MaxInstanceBlocks * 1024]
    };
    private int drawCount, maxInstanceIndex;

    private void SetupFreelist()
    {
        this.drawCount = 0; this.maxInstanceIndex = 0;
        for(int i = 0; i < MaxInstanceBlocks; i++)
        {
            for(int j = 0; j < 1023; j++) this.freeList.Add(i * 1023 + j);
            // this.drawInstances[i] = new InstanceBlock { pos1Array = new Vector4[1023], pos2Array = new Vector4[1023], colorSampleCoordArray = new Vector4[1023] };
        }
    }
    private void UpdateAllParticles()
    {
        this.drawCount = 0;
        for(int i = 0; i < this.maxInstanceIndex; i++)
        {
            if(this.instanceStore[i] != null)
            {
                var lifetime = Time.time - this.instanceStore[i].spawnTime;
                if(lifetime >= 1.0f) { this.instanceStore[i] = null; this.freeList.Add(i); if(i == this.maxInstanceIndex - 1) this.maxInstanceIndex--; }
                else
                {
                    var currentSpeed = this.instanceStore[i].initialSpeedFactor * Mathf.Pow(Mathf.Abs(this.instanceStore[i].reductionRate), lifetime * 60.0f);
                    this.instanceStore[i].position = this.instanceStore[i].forward * (currentSpeed * this.instanceStore[i].speedMag * 3.0f * Time.deltaTime) + this.instanceStore[i].position;

                    /*this.drawInstances[this.drawCount / 1023].pos1Array[this.drawCount % 1023] = this.instanceStore[i].position;
                    this.drawInstances[this.drawCount / 1023].pos2Array[this.drawCount % 1023] = this.instanceStore[i].forward *
                        ((currentSpeed + 0.0625f) * this.instanceStore[i].lengthMag / 10.0f) + this.instanceStore[i].position;
                    this.drawInstances[this.drawCount / 1023].colorSampleCoordArray[this.drawCount % 1023] = new Vector4(lifetime, this.instanceStore[i].colorSamplingV, 0.0f, 0.0f);*/
                    this.drawInstance.pos1Array[this.drawCount] = this.instanceStore[i].position;
                    this.drawInstance.pos2Array[this.drawCount] = this.instanceStore[i].forward *
                        ((currentSpeed + 0.0625f) * this.instanceStore[i].lengthMag / 10.0f) + this.instanceStore[i].position;
                    this.drawInstance.colorSampleCoordArray[this.drawCount] = new Vector4(lifetime, this.instanceStore[i].colorSamplingV, 0.0f, 0.0f);
                    this.drawCount++;
                }
            }
        }
    }
    private void DrawParticles()
    {
        for(int i = 0; i < this.drawCount; i++)
        {
            this._renderer.SetPass(0);
            this._renderer.SetVector(_Pos1, this.drawInstance.pos1Array[i]);
            this._renderer.SetVector(_Pos2, this.drawInstance.pos2Array[i]);
            this._renderer.SetVector(_ColorSampleCoord, this.drawInstance.colorSampleCoordArray[i]);
            Graphics.DrawProcedural(MeshTopology.Lines, 2, 1);
        }
        /*for(int i = 0; i < this.drawCount; i++)
        {
            this._renderer.SetPass(0);
            this._renderer.SetVector(_Pos1, this.drawInstances[i / 1023].pos1Array[i % 1023]);
            this._renderer.SetVector(_Pos2, this.drawInstances[i / 1023].pos2Array[i % 1023]);
            this._renderer.SetVector(_ColorSampleCoord, this.drawInstances[i / 1023].colorSampleCoordArray[i % 1023]);
            Graphics.DrawProcedural(MeshTopology.Lines, 2, 1);
        }*/
        /*if(this.freeList.Peek() % 1023 != 0)
        {
            this._renderer.SetVectorArray(_Pos1, this.drawInstances[this.freeList.Peek() / 1023 + 1].pos1Array);
            this._renderer.SetVectorArray(_Pos2, this.drawInstances[this.freeList.Peek() / 1023 + 1].pos2Array);
            this._renderer.SetVectorArray(_ColorSampleCoord, this.drawInstances[this.freeList.Peek() / 1023 + 1].colorSampleCoordArray);
            Graphics.DrawProcedural(MeshTopology.Lines, 2, this.freeList.Peek() % 1023);
        }*/
    }

    /// Implements Minimum Binary Heap
    public sealed class BinaryHeap<T> where T: System.IComparable<T>
    {
        private List<T> elements = new List<T> { default(T) };

        public bool IsEmpty { get { return this.elements.Count <= 1; } }

        public void Clear() { this.elements.Clear(); this.elements.Add(default(T)); }
        public void Add(T value)
        {
            this.elements.Add(value);

            var n = this.elements.Count - 1;
            while(n > 1 && this.elements[n].CompareTo(this.elements[n / 2]) < 0)
            {
                this.SwapElements(n, n / 2);
                n /= 2;
            }
        }
        public T Peek() => this.elements[1];
        public T Extract()
        {
            var value = this.Peek();
            this.elements[1] = this.elements[this.elements.Count - 1];
            this.elements.RemoveAt(this.elements.Count - 1);

            var n = 1;
            while(true)
            {
                var largest = n; var left = n * 2; var right = n * 2 + 1;
                if(this.CompareLessInElements(left, n) || this.CompareLessInElements(right, n))
                {
                    largest = this.ComputeValueLessInElements(right, left, () => right, () => left);
                }
                if(largest != n)
                {
                    this.SwapElements(n, largest);
                    n = largest;
                }
                else break;
            }
            return value;
        }

        public void AssertSatisfyInvariant()
        {
            this.AssertSatisfyInvariantForNode(1);
        }
        private void AssertSatisfyInvariantForNode(int n)
        {
            var left = n * 2; var right = left + 1;
            if(this.elements.Count > left)
            {
                Debug.Assert(this.elements[n].CompareTo(this.elements[left]) < 0);
                this.AssertSatisfyInvariantForNode(left);
            }
            if(this.elements.Count > right)
            {
                Debug.Assert(this.elements[n].CompareTo(this.elements[right]) < 0);
                this.AssertSatisfyInvariantForNode(right);
            }
        }

        private void SwapElements(int a, int b)
        {
            var x = this.elements[a];
            this.elements[a] = this.elements[b];
            this.elements[b] = x;
        }
        /// <summary>
        /// Compares the element indexed by `g` is larger value than the element indexed by `t`.
        /// Returns false when index `g` or `t` is not in `elements`.
        /// </summary>
        private bool CompareLessInElements(int g, int t)
        {
            if(this.elements.Count > g && this.elements.Count > t)
            {
                return this.elements[g].CompareTo(this.elements[t]) < 0;
            }
            else { return false; }
        }
        private U ComputeValueLessInElements<U>(int g, int t, System.Func<U> g_func, System.Func<U> t_func)
        {
            if(this.elements.Count > g)
            {
                if(this.elements.Count >= t) return this.elements[g].CompareTo(this.elements[t]) < 0 ? g_func() : t_func();
                else return g_func();
            }
            else if(this.elements.Count > t) return t_func();
            else { Debug.Assert(false); return default(U); }
        }
    }
}
