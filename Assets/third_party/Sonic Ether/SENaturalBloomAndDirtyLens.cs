using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Sonic Ether/SE Natural Bloom and Dirty Lens")]
public class SENaturalBloomAndDirtyLens : MonoBehaviour
{
	
	public Shader shader;
	private Material material;
	private bool isSupported;
	Camera cam;
	
	
	
	[Range(0.0f, 0.4f)]
	public float bloomIntensity = 0.05f;
	[Range(0.0f, 1.0f)]
	public float bloomScatterFactor = 0.5f;
	[Range(0.0f, 1.0f)]
	public float lensDirtScatterFactor = 0.5f;
	[Range(0.0f, 0.95f)]
	public float lensDirtIntensity = 0.05f;
	public Texture2D lensDirtTexture;



	public enum DepthBlendFunction
	{
		Exponential,
		ExponentialSquared
	};


	public bool inputIsHDR;
	public bool lowQuality = false;
	// Set by passthroughControl: keep the frame's alpha so the bloom works over passthrough.
	public bool preserveAlpha = false;

	public bool depthBlending = false;
	public DepthBlendFunction depthBlendFunction = DepthBlendFunction.Exponential;
	[Range(0.0f, 1.0f)]
	public float depthBlendFactor = 0.1f;
	[Range(0.0f, 1.0f)]
	public float maxDepthBlendFactor = 1.0f;
	[Range(0.0f, 1.0f)]
	public float depthScatterFactor = 0.5f;

	void OnEnable() 
	{
		isSupported = true;

		if (!material && shader)
			material = new Material(shader);

		if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
		{
			isSupported = false;
		}

		cam = GetComponent<Camera>();
	}

	void OnDisable()
	{
		if(material)
			DestroyImmediate(material);
	}
	
	// Builds a temporary-RT descriptor that matches the source's layout. Under Multiview /
	// single-pass stereo the source is a 2-slice texture array, so intermediates must be too.
	static RenderTexture GetTempLike(RenderTexture source, int width, int height)
	{
		RenderTextureDescriptor desc = source.descriptor;
		desc.width = Mathf.Max(1, width);
		desc.height = Mathf.Max(1, height);
		desc.depthBufferBits = 0;
		desc.msaaSamples = 1;
		desc.useMipMap = false;
		desc.autoGenerateMips = false;
		RenderTexture rt = RenderTexture.GetTemporary(desc);
		rt.filterMode = FilterMode.Bilinear;
		return rt;
	}

	void OnRenderImage(RenderTexture source, RenderTexture destination) 
	{
		if (!isSupported)
		{
			Graphics.Blit(source, destination);
			return;
		}


		#if UNITY_EDITOR
		if (source.format == RenderTextureFormat.ARGBHalf)
			inputIsHDR = true;
		else
			inputIsHDR = false;
		#endif

		material.hideFlags = HideFlags.HideAndDontSave;

		material.SetFloat("_BloomIntensity", Mathf.Exp(bloomIntensity) - 1.0f);
		material.SetFloat("_LensDirtIntensity", Mathf.Exp(lensDirtIntensity) - 1.0f);
		material.SetFloat("_DepthBlendFactor", depthBlending ? Mathf.Pow(depthBlendFactor, 2.0f) : 0.0f);
		material.SetFloat("_MaxDepthBlendFactor", maxDepthBlendFactor);
		material.SetFloat("_DepthScatterFactor", depthScatterFactor * 4.0f - 1.0f);
		material.SetFloat("_BloomScatterFactor", bloomScatterFactor * 5.0f - 2.5f);
		material.SetFloat("_LensDirtScatterFactor", lensDirtScatterFactor * 5.0f - 2.5f);
		material.SetFloat("_PreserveAlpha", preserveAlpha ? 1.0f : 0.0f);
		material.SetInt("_DepthBlendFunction", depthBlendFunction == DepthBlendFunction.Exponential ? 0 : 1);
		material.SetMatrix("ProjectionMatrixInverse", cam.projectionMatrix.inverse);

		source.filterMode = FilterMode.Bilinear;

		RenderTexture clampedSource = GetTempLike(source, source.width, source.height);
		Graphics.Blit(source, clampedSource, material, 5);

		int initialDivisor = lowQuality ? 4 : 2;

		int rtWidth = source.width / initialDivisor;
		int rtHeight = source.height / initialDivisor;

		RenderTexture downsampled;
		downsampled = clampedSource;



		int iterations = 1;

		int octaves = lowQuality ? 4 : 8;

		for (int i = 0; i < octaves; i++)
		{
			RenderTexture rt = GetTempLike(source, rtWidth, rtHeight);
			rt.filterMode = FilterMode.Bilinear;

			Graphics.Blit(downsampled, rt, material, 1);


			if (i >= 1)
			{
				iterations = 2;
			}

			for (int j = 0; j < iterations; j++)
			{

				//vertical blur
				RenderTexture rt2 = GetTempLike(source, rtWidth, rtHeight);
				rt2.filterMode = FilterMode.Bilinear;
				Graphics.Blit(rt, rt2, material, 2);
				RenderTexture.ReleaseTemporary(rt);
				rt = rt2;

				rt2 = GetTempLike(source, rtWidth, rtHeight);
				rt2.filterMode = FilterMode.Bilinear;
				Graphics.Blit(rt, rt2, material, 3);
				RenderTexture.ReleaseTemporary(rt);
				rt = rt2;
			}

			downsampled = rt;

			switch (i)
			{
				case 0:
					Shader.SetGlobalTexture("_Bloom0", rt);
					break;
				case 1:
					Shader.SetGlobalTexture("_Bloom1", rt);
					break;
				case 2:
					Shader.SetGlobalTexture("_Bloom2", rt);
					break;
				case 3:
					Shader.SetGlobalTexture("_Bloom3", rt);
					break;
				case 4:
					Shader.SetGlobalTexture("_Bloom4", rt);
					break;
				case 5:
					Shader.SetGlobalTexture("_Bloom5", rt);
					break;
				case 6:
					Shader.SetGlobalTexture("_Bloom6", rt);
					break;
				case 7:
					Shader.SetGlobalTexture("_Bloom7", rt);
					break;
				default: 
					break;
			}

			RenderTexture.ReleaseTemporary(rt);

			rtWidth /= lowQuality ? 3 : 2;
			rtHeight /= lowQuality ? 3 : 2;
		}


		material.SetTexture("_LensDirt", lensDirtTexture);
		Graphics.Blit(clampedSource, destination, material, lowQuality ? 4 : 0);
		RenderTexture.ReleaseTemporary(clampedSource);

	}
}