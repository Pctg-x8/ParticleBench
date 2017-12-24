using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

public sealed class GPULineParticleDriver : MonoBehaviour, IParticleDriver
{
    public static GPULineParticleDriver Self { get; private set; }
    void Awake() { Self = this; }

    /// GPU Compatible SpawnData Structure
    [StructLayout(LayoutKind.Explicit, Size = 4 * 4 * 3)]
    public struct SpawnData
    {
        [FieldOffset(0)] public Vector3 forward;
        [FieldOffset(12)] public float colorSamplingV;
        [FieldOffset(16)] public Vector4 position;
        [FieldOffset(32)] public float initialSpeedFactor;
        [FieldOffset(36)] public float reductionRate;
        [FieldOffset(40)] public float speedMag;
        [FieldOffset(44)] public float lengthMag;
    }

    private const int MaxInstances = 1024 * 2048;
    private enum KernIndex : int { InitReservedSlots, SpawnParticles, UpdateParticles };
    private static readonly int GameTimeID = Shader.PropertyToID("gametime");
    private static readonly int DeltaTimeID = Shader.PropertyToID("deltaTime");
    private static readonly int SpawnerCountID = Shader.PropertyToID("spawnerCount");
    private static readonly int ReservedSlotsID = Shader.PropertyToID("reservedSlots");
    private static readonly int ReservedSlotsAppID = Shader.PropertyToID("reservedSlotsApp");
    private static readonly int InstancePropertiesID = Shader.PropertyToID("instanceProperties");
    private static readonly int InstanceDrawingDataID = Shader.PropertyToID("instanceDrawingData");
    private static readonly int ColorGradientTextureID = Shader.PropertyToID("_ColorGradient");
    private static readonly int SpawnArgsID = Shader.PropertyToID("spawnArgs");
    [SerializeField] private ComputeShader computation;
    [SerializeField] private Material _renderer;
    private int spawnParticlesKern, updateParticlesKern;
    private ComputeBuffer reservedSlots, instanceProperties, instanceDrawingData, drawArgs;
    private SpawnData[] spawnDataStore;
    private int spawnDataCount;
    private ComputeBuffer spawnArgs;
    private CommandBuffer renderCommands;
    private int gradientTextureHeight;

    void OnEnable()
    {
        this.reservedSlots = new ComputeBuffer(MaxInstances, sizeof(int), ComputeBufferType.Append);
        this.reservedSlots.SetCounterValue(0);
        this.instanceProperties = new ComputeBuffer(MaxInstances, 4 * 4 * sizeof(float));
        this.instanceDrawingData = new ComputeBuffer(MaxInstances, 3 * 4 * sizeof(float), ComputeBufferType.Append);
        this.drawArgs = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
        this.drawArgs.SetData(new int[] { 2, 0, 0, 0 });
        this.spawnDataStore = new SpawnData[MaxInstances];
        this.spawnDataCount = 0;
        this.spawnArgs = new ComputeBuffer(MaxInstances, Marshal.SizeOf<SpawnData>());

        this.gradientTextureHeight = this._renderer.GetTexture(ColorGradientTextureID).height;
        this.computation.SetBuffer((int)KernIndex.InitReservedSlots, ReservedSlotsAppID, this.reservedSlots);
        this.computation.Dispatch((int)KernIndex.InitReservedSlots, MaxInstances >> 10, 1, 1);

        this._renderer.SetBuffer(InstanceDrawingDataID, this.instanceDrawingData);
        this.renderCommands = new CommandBuffer { name = "GPU Line Particle Drawing" };
        this.renderCommands.DrawProceduralIndirect(this.GetComponent<Camera>().worldToCameraMatrix,
            this._renderer, 0, MeshTopology.Lines, this.drawArgs, 0);
        this.GetComponent<Camera>().AddCommandBuffer(CameraEvent.BeforeImageEffects, this.renderCommands);
    }
    void OnDisable()
    {
        this.GetComponent<Camera>().RemoveCommandBuffer(CameraEvent.BeforeImageEffects, this.renderCommands);
        this.renderCommands.Release(); this.renderCommands = null;
        this.drawArgs.Release(); this.instanceDrawingData.Release(); this.instanceProperties.Release();
        this.reservedSlots.Release(); this.spawnArgs.Release(); this.spawnDataStore = null;
    }

    private static Vector3 ForwardVec(float th) => new Vector3(Mathf.Sin(th), Mathf.Cos(th), 0.0f);
    private float ColorSamplingV(int gindex) => (gindex + 0.5f) / this.gradientTextureHeight;
    public void Spawn(Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f)
    {
        var newIndex = this.spawnDataCount++;
        this.spawnDataStore[newIndex] = new SpawnData
        {
            forward = ForwardVec(Random.Range(0.0f, Mathf.PI * 2.0f)), position = new Vector4(at.x, at.y, at.z, 1.0f),
            colorSamplingV = ColorSamplingV(gradientIndex), initialSpeedFactor = Random.Range(0.25f, 1.5f),
            reductionRate = reductionRate, speedMag = speedMag, lengthMag = lengthMag
        };
    }
    public void Spawn(int count, Vector3 at, int gradientIndex, float speedMag = 4.0f, float lengthMag = 6.0f, float reductionRate = 0.9375f)
    {
        var pos = new Vector4(at.x, at.y, at.z, 1.0f);
        var cv = ColorSamplingV(gradientIndex);
        var startIndex = this.spawnDataCount;
        this.spawnDataCount += count;
        while(count > 0)
        {
            count--;
            this.spawnDataStore[startIndex + count] = new SpawnData
            {
                speedMag = speedMag, lengthMag = lengthMag, colorSamplingV = cv, reductionRate = reductionRate,
                forward = ForwardVec(Random.Range(0.0f, Mathf.PI * 2.0f)), position = pos,
                initialSpeedFactor = Random.Range(0.25f, 1.5f)
            };
        }
    }

    void OnPreRender()
    {
        this.computation.SetFloat(GameTimeID, Time.time);
        this.computation.SetFloat(DeltaTimeID, Time.deltaTime);
        if(this.spawnDataCount > 0)
        {
            Profiler.BeginSample("Update Spawn Data");
            this.spawnArgs.SetData(new System.ArraySegment<SpawnData>(this.spawnDataStore, 0, this.spawnDataCount).ToArray());
            this.computation.SetBuffer((int)KernIndex.SpawnParticles, ReservedSlotsID, this.reservedSlots);
            this.computation.SetBuffer((int)KernIndex.SpawnParticles, SpawnArgsID, this.spawnArgs);
            this.computation.SetBuffer((int)KernIndex.SpawnParticles, InstancePropertiesID, this.instanceProperties);
            this.computation.SetInt(SpawnerCountID, this.spawnDataCount);
            this.computation.Dispatch((int)KernIndex.SpawnParticles, (this.spawnDataCount + 0xff) >> 8, 1, 1);
            this.spawnDataCount = 0;
            Profiler.EndSample();
        }
        this.instanceDrawingData.SetCounterValue(0);
        this.computation.SetBuffer((int)KernIndex.UpdateParticles, InstancePropertiesID, this.instanceProperties);
        this.computation.SetBuffer((int)KernIndex.UpdateParticles, InstanceDrawingDataID, this.instanceDrawingData);
        this.computation.SetBuffer((int)KernIndex.UpdateParticles, ReservedSlotsAppID, this.reservedSlots);
        this.computation.Dispatch((int)KernIndex.UpdateParticles, MaxInstances >> 10, 1, 1);
        ComputeBuffer.CopyCount(this.instanceDrawingData, this.drawArgs, sizeof(int));

        /*var args = new int[7];
        this.drawArgs.GetData(args);
        Debug.Log("Draw Args: " + string.Join(", ", args));*/
    }
}
