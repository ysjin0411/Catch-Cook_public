using SushiPouringV3;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

/// <summary>읽기 전용 입자를 GPU 밀도장으로 그리고 연결된 액체로 합성하는 View입니다.</summary>
public sealed class DrinkFluidRendererView : MonoBehaviour
{
    // 셰이더는 직렬화된 참조로 보존하여 빌드에서도 포함합니다.
    [SerializeField] Shader _densityShader, _compositeShader;
    [SerializeField] UnityEngine.UI.RawImage _output;
    // 표시 영역 비율과 같은 해상도이며 밀도는 선형 HDR 데이터입니다.
    [SerializeField, Range(128,1024)] int _textureHeight = 600;
    [SerializeField, Range(.03f,.5f)] float _renderRadius = .21f;
    // 컵 벽 두께를 표현하기 위해 표시 경계를 안쪽으로 줄이는 계산 좌표 단위 여백입니다.
    [SerializeField, Min(0f)] float _cupSideInset = .06f;
    // 컵 바닥 두께를 표현하기 위해 액체 표시의 하단을 올리는 여백입니다.
    [SerializeField, Min(0f)] float _cupBottomInset = .10f;
    // 공중 입자의 속도 방향으로만 커널을 늘려 물줄기의 연결감을 조절합니다.
    [SerializeField, Range(0,.2f)] float _flyingStretchTime = .10f;
    [SerializeField, Range(1,4)] float _maximumFlyingStretch = 2.5f;
    [SerializeField, Range(.05f,2)] float _threshold = .5f;
    [SerializeField, Range(.001f,.3f)] float _minimumEdgeWidth = .025f;
    [SerializeField, Range(0,3)] float _edgeAAScale = 1;
    [SerializeField] Color _liquidColor = new Color(.88f,.72f,.14f,1);
    [SerializeField] Color _highlightColor = new Color(1,1,.75f,1);
    [SerializeField, Range(0,3)] float _normalStrength = .7f, _highlightStrength = .45f;
    [SerializeField, Range(1,32)] float _highlightPower = 8;
    [SerializeField] Vector2 _lightDirection = new Vector2(-.6f,.8f);
    [SerializeField] bool _densityDebug, _particleDebug;
    Mesh _mesh;
    Material _densityMaterial, _compositeMaterial;
    RenderTexture _density;
    CommandBuffer _commands;
    Vector3[] _vertices;
    Vector2[] _uv, _ratios;
    int[] _indices;
    FluidSimulationSettings _settings;
    // CPU와 같은 등간격 경계를 저장하는 선형 데이터 텍스처입니다.
    Texture2D _cupProfileTexture;
    int _capacity;
    public RenderTexture DensityTexture => _density;
    public int RenderedParticleCount { get; private set; }

    /// <summary>물방울과 컵 안 액체에 사용할 본색과 표면 반사색을 변경합니다.</summary>
    public void SetColors(Color liquidColor, Color highlightColor)
    {
        // 다음 Render에서 사용할 액체 본색을 보관합니다.
        _liquidColor = liquidColor;
        // 다음 Render에서 사용할 표면 반사색을 보관합니다.
        _highlightColor = highlightColor;
    }

    /// <summary>라운드의 최대 용량에 맞춰 버퍼와 렌더 자원을 확보합니다.</summary>
    public void Prepare(int capacity, FluidSimulationSettings settings)
    {
        ReleaseResources(); _settings = settings; _capacity = capacity;
        if (_output == null || _densityShader == null || _compositeShader == null)
            throw new System.InvalidOperationException("음료 렌더러의 RawImage와 셰이더 연결이 필요합니다.");
        _vertices = new Vector3[capacity*4]; _uv = new Vector2[capacity*4];
        _ratios = new Vector2[capacity*4]; _indices = new int[capacity*6];
        for (int i=0;i<capacity;i++)
        {
            int v=i*4, t=i*6;
            _uv[v]=new Vector2(-1,-1); _uv[v+1]=new Vector2(-1,1);
            _uv[v+2]=new Vector2(1,1); _uv[v+3]=new Vector2(1,-1);
            _indices[t]=v; _indices[t+1]=v+1; _indices[t+2]=v+2;
            _indices[t+3]=v; _indices[t+4]=v+2; _indices[t+5]=v+3;
        }
        _mesh=new Mesh {name="DrinkDensityMesh", hideFlags=HideFlags.DontSave,
            indexFormat=capacity*4>65535?IndexFormat.UInt32:IndexFormat.UInt16}; _mesh.MarkDynamic();
        _densityMaterial=new Material(_densityShader){hideFlags=HideFlags.DontSave};
        _compositeMaterial=new Material(_compositeShader){hideFlags=HideFlags.DontSave};
        // 이번 라운드의 경계는 한 번만 업로드합니다.
        PrepareCupProfile(settings);
        // R에는 전체 밀도, G에는 공중 입자 밀도를 저장해 컵 밖 물방울도 표시합니다.
        GraphicsFormat format = GraphicsFormat.R16G16_SFloat;
        if (!Supports(format)) format=GraphicsFormat.R16G16B16A16_SFloat;
        if (!Supports(format)) throw new System.NotSupportedException("HDR 밀도 누적 포맷을 지원하지 않습니다.");
        int height=Mathf.Clamp(_textureHeight,128,1024);
        int width=Mathf.Max(1,Mathf.RoundToInt(height*(float)((settings.ViewMaxX-settings.ViewMinX)/(settings.ViewMaxY-settings.ViewMinY))));
        _density=new RenderTexture(new RenderTextureDescriptor(width,height){graphicsFormat=format,
            depthBufferBits=0,msaaSamples=1,useMipMap=false}){name="DrinkDensity",hideFlags=HideFlags.DontSave,
            filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        _density.Create();
        _commands=new CommandBuffer {name="Drink density accumulation"};
        _output.texture=_density; _output.material=_compositeMaterial;
        _output.color=Color.white; _output.raycastTarget=false; _output.maskable=false;
        Clear();
    }

    /// <summary>필요한 렌더·샘플·가산 블렌딩 지원을 함께 검사합니다.</summary>
    static bool Supports(GraphicsFormat format) => SystemInfo.IsFormatSupported(format,FormatUsage.Render) &&
        SystemInfo.IsFormatSupported(format,FormatUsage.Sample) && SystemInfo.IsFormatSupported(format,FormatUsage.Blend);

    /// <summary>복사받은 유효 입자만 재사용 메시로 옮기고 밀도를 가산합니다.</summary>
    public void Render(FluidParticle[] particles, int count)
    {
        if (_mesh==null || _density==null) return;
        if (!_density.IsCreated()) _density.Create();
        count=Mathf.Clamp(count,0,Mathf.Min(_capacity,particles.Length)); RenderedParticleCount=count;
        float sx=(float)(_settings.ViewMaxX-_settings.ViewMinX), sy=(float)(_settings.ViewMaxY-_settings.ViewMinY);
        for(int i=0;i<count;i++)
        {
            Vector2 velocity=new Vector2((float)particles[i].VelocityX,(float)particles[i].VelocityY);
            Vector2 axis=velocity.sqrMagnitude>.000001f?velocity.normalized:Vector2.up;
            Vector2 side=new Vector2(axis.y,-axis.x);
            float radius=_particleDebug?Mathf.Min(.03f,_renderRadius):_renderRadius;
            float length=_particleDebug?radius:
                Mathf.Clamp(velocity.magnitude*_flyingStretchTime,radius,radius*_maximumFlyingStretch);
            for(int c=0;c<4;c++)
            {
                int v=i*4+c;
                Vector2 offset=side*(_uv[v].x*radius)+axis*(_uv[v].y*length);
                _vertices[v]=new Vector3(((float)particles[i].X+offset.x-(float)_settings.ViewMinX)/sx*2-1,
                    ((float)particles[i].Y+offset.y-(float)_settings.ViewMinY)/sy*2-1,0);
                // 컵 내부 입자만 컵 마스크로 제한하고 공중 입자는 낙하 중에도 표시합니다.
                _ratios[v]=new Vector2((float)particles[i].VolumeRatio,particles[i].IsInCup?0f:1f);
            }
        }
        _mesh.Clear(false); _mesh.vertices=_vertices; _mesh.uv=_uv; _mesh.uv2=_ratios;
        _mesh.SetIndices(_indices,0,count*6,MeshTopology.Triangles,0,false);
        _mesh.bounds=new Bounds(Vector3.zero,Vector3.one*100);
        _densityMaterial.SetFloat("_ClipYSign",SystemInfo.graphicsUVStartsAtTop?-1:1);
        _compositeMaterial.SetColor("_LiquidColor",_liquidColor);
        _compositeMaterial.SetColor("_HighlightColor",_highlightColor);
        _compositeMaterial.SetVector("_Lighting",new Vector4(_lightDirection.x,_lightDirection.y,_normalStrength,_highlightStrength));
        _compositeMaterial.SetVector("_Surface",new Vector4(_threshold,_minimumEdgeWidth,_edgeAAScale,_highlightPower));
        _compositeMaterial.SetVector("_ViewSize",new Vector4(sx,sy,_densityDebug?1:0,_particleDebug?1:0));
        // 현재 컵 경계를 전달하여 컵 밖의 액체 표시를 제한합니다.
        ApplyCupClipSettings();
        _commands.Clear(); _commands.SetRenderTarget(_density);
        _commands.SetViewport(new Rect(0,0,_density.width,_density.height));
        _commands.ClearRenderTarget(false,true,Color.clear);
        if(count>0) _commands.DrawMesh(_mesh,Matrix4x4.identity,_densityMaterial,0,0);
        Graphics.ExecuteCommandBuffer(_commands);
    }

    /// <summary>시뮬레이션의 표시 범위와 컵 경계를 합성 셰이더에 전달합니다.</summary>
    private void ApplyCupClipSettings()
    {
        // UV 좌표를 시뮬레이션 좌표로 변환할 때 사용할 범위를 전달합니다.
        _compositeMaterial.SetVector("_SimulationBounds", new Vector4(
            (float)_settings.ViewMinX,
            (float)_settings.ViewMinY,
            (float)_settings.ViewMaxX,
            (float)_settings.ViewMaxY));

        // 컵 바닥 높이, 입구 높이, 바닥 반너비, 입구 반너비를 전달합니다.
        _compositeMaterial.SetVector("_CupBounds", new Vector4(
            (float)_settings.Bottom,
            (float)_settings.Rim,
            (float)_settings.BottomHalfWidth,
            (float)_settings.TopHalfWidth));

        // 컵의 원래 기울기는 유지하면서 옆면과 바닥의 표시 여백만 전달합니다.
        _compositeMaterial.SetVector("_CupInsets", new Vector4(
            Mathf.Max(0f, _cupSideInset),
            Mathf.Max(0f, _cupBottomInset), 0f, 0f));
    }

    /// <summary>물리에서 사용하는 좌우 경계를 셰이더 조회 텍스처로 만듭니다.</summary>
    private void PrepareCupProfile(FluidSimulationSettings settings)
    {
        // 프로파일이 없는 기존 컵도 같은 경로로 표시합니다.
        FluidCupGeometry cup = new FluidCupGeometry(settings);
        int count = Mathf.Max(2, settings.ProfileCount);
        _cupProfileTexture = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
        _cupProfileTexture.name = "DrinkCupProfile";
        _cupProfileTexture.hideFlags = HideFlags.DontSave;
        _cupProfileTexture.wrapMode = TextureWrapMode.Clamp;
        _cupProfileTexture.filterMode = FilterMode.Bilinear;
        // 양 끝 표본이 바닥과 입구에 정확히 대응하도록 작성합니다.
        Color[] pixels = new Color[count];
        for (int i = 0; i < count; i++)
        {
            double y = cup.Bottom + (cup.Rim - cup.Bottom) * i / (count - 1d);
            pixels[i] = new Color((float)cup.LeftAt(y), (float)cup.RightAt(y), 0f, 1f);
        }
        // 프로파일은 측정 단계에서 벽 여백을 포함하므로 추가로 축소하지 않습니다.
        _cupProfileTexture.SetPixels(pixels);
        _cupProfileTexture.Apply(false, true);
        _compositeMaterial.SetTexture("_CupProfile", _cupProfileTexture);
        _compositeMaterial.SetFloat("_CupProfileCount", count);
        _compositeMaterial.SetFloat("_HasCupProfile", settings.ProfileCount > 0 ? 1f : 0f);
    }

    /// <summary>이전 라운드의 밀도와 표시 개수를 지우며 물리 데이터는 변경하지 않습니다.</summary>
    public void Clear()
    {
        RenderedParticleCount=0;
        if(_commands==null || _density==null) return;
        _commands.Clear(); _commands.SetRenderTarget(_density); _commands.ClearRenderTarget(false,true,Color.clear);
        Graphics.ExecuteCommandBuffer(_commands);
    }

    /// <summary>이 컴포넌트가 소유한 메시·RT·머티리얼만 해제합니다.</summary>
    public void ReleaseResources()
    {
        // 라운드별 경계 텍스처도 다른 렌더 자원과 함께 정리합니다.
        if (_cupProfileTexture != null) { Destroy(_cupProfileTexture); _cupProfileTexture = null; }
        if(_output!=null) { _output.texture=null; _output.material=null; }
        if(_commands!=null) { _commands.Release(); _commands=null; }
        if(_density!=null) { _density.Release(); Destroy(_density); _density=null; }
        if(_mesh!=null) { Destroy(_mesh); _mesh=null; }
        if(_densityMaterial!=null) { Destroy(_densityMaterial); _densityMaterial=null; }
        if(_compositeMaterial!=null) { Destroy(_compositeMaterial); _compositeMaterial=null; }
        _vertices=null; _uv=null; _ratios=null; _indices=null; RenderedParticleCount=0;
    }

    /// <summary>오브젝트 파괴 시에도 직접 소유한 렌더 자원을 해제합니다.</summary>
    void OnDestroy() { ReleaseResources(); }
}

