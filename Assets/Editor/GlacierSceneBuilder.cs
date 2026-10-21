using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class GlacierSceneBuilder
{
    const string ScenePath = "Assets/Scenes/RussellGlacier.unity";
    const string TerrainRoot = "Assets/Terrain/";
    const string MaterialsRoot = "Assets/Materials/";

    [Serializable]
    public class TerrainInfo
    {
        public int resolution;
        public float size;
        public float baseElevation;
        public float heightRange;
        public float spawnX;
        public float spawnZ;
        public float spawnElevation;
        public float spawnSlopeDegrees;
        public double centerEasting;
        public double centerNorthing;
    }

    [MenuItem("Halation/Open glacier scene")]
    public static void Open()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath);
    }

    public static void Build()
    {
        var info = JsonUtility.FromJson<TerrainInfo>(File.ReadAllText(TerrainRoot + "Data/terrain.json"));
        Directory.CreateDirectory(TerrainRoot);
        Directory.CreateDirectory(MaterialsRoot);
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Terrain terrain = MakeTerrain("Russell Glacier - 10 cm", info.resolution, info.size, info.heightRange, Vector3.zero);
        terrain.heightmapPixelError = 1;

        var rover = new GameObject("Rover");
        var body = rover.AddComponent<Rigidbody>();
        body.mass = 100;
        body.linearDamping = .05f;
        body.angularDamping = .5f;
        var box = rover.AddComponent<BoxCollider>();
        box.size = new Vector3(.96f, 1.46f, 1.46f);
        var frame = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/Mars Rover Chassis Assembly.obj"));
        frame.transform.SetParent(rover.transform, false);
        frame.transform.localRotation = Quaternion.Euler(0, -90, 0);
        var aluminum = MaterialAsset("Frame", new Color(.37f, .43f, .46f), .7f, .4f);
        foreach (var renderer in frame.GetComponentsInChildren<Renderer>())
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = aluminum;
            renderer.sharedMaterials = mats;
        }
        var drive = rover.AddComponent<RoverPhysicsDrive>();
        var rubber = MaterialAsset("Tires", new Color(.045f, .052f, .06f), 0, .2f);
        var wheels = new List<RoverPhysicsDrive.Wheel>();
        foreach (float z in new[] { -.55f, .55f })
            foreach (float side in new[] { -1f, 1f })
            {
                var go = new GameObject((z > 0 ? "Front" : "Rear") + (side < 0 ? " left wheel" : " right wheel"));
                go.transform.SetParent(rover.transform, false);
                go.transform.localPosition = new Vector3(side * .65f, -.65f, z);
                var wheel = go.AddComponent<WheelCollider>();
                wheel.radius = .26f;
                wheel.mass = 3;
                wheel.suspensionDistance = .18f;
                wheel.wheelDampingRate = 1.0f;
                var spring = wheel.suspensionSpring;
                spring.spring = 8000;
                spring.damper = 1000;
                spring.targetPosition = .5f;
                wheel.suspensionSpring = spring;
                wheel.forceAppPointDistance = .18f;
                var visual = new GameObject("Wheel visual");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = -Vector3.up * .06f;
                Primitive(
                    "Tire",
                    PrimitiveType.Cylinder,
                    visual.transform,
                    Vector3.zero,
                    new Vector3(.52f, .09f, .52f),
                    Quaternion.Euler(0, 0, 90),
                    rubber);
                wheels.Add(new RoverPhysicsDrive.Wheel { collider = wheel, visual = visual.transform, left = side < 0 });
            }
        drive.wheels = wheels.ToArray();
        var movement = rover.AddComponent<RoverMovement>();
        movement.controlMode = RoverMovement.ControlMode.Keyboard;
        var ros = rover.AddComponent<RoverRosClient>();
        Vector3 spawn = new Vector3(info.spawnX, info.spawnElevation + 1.12f, info.spawnZ);
        rover.transform.position = spawn;

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 57;
        camera.nearClipPlane = .08f;
        camera.farClipPlane = 1400;
        cameraObject.AddComponent<AudioListener>();
        var follow = cameraObject.AddComponent<GlacierCamera>();
        follow.target = rover.transform;
        follow.terrain = terrain;
        camera.transform.position = spawn + new Vector3(0, 2.75f, -5.5f);
        camera.transform.LookAt(spawn + Vector3.up * .25f);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = .9f;
        light.color = new Color(1, .96f, .89f);
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(32, -35, 0);
        RenderSettings.sun = light;

        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetColor("_SkyTint", new Color(.63f, .72f, .8f));
        sky.SetColor("_GroundColor", new Color(.34f, .39f, .43f));
        sky.SetFloat("_AtmosphereThickness", .75f);
        sky.SetFloat("_Exposure", 1.0f);
        SaveAsset(sky, "Sky.mat");
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.58f, .66f, .75f);
        RenderSettings.ambientEquatorColor = new Color(.38f, .43f, .48f);
        RenderSettings.ambientGroundColor = new Color(.23f, .25f, .28f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 180;
        RenderSettings.fogEndDistance = 650;
        RenderSettings.fogColor = new Color(.70f, .77f, .82f);
        QualitySettings.shadowDistance = 100;
        QualitySettings.antiAliasing = 4;

        var session = new GameObject("Glacier simulation").AddComponent<GlacierSession>();
        session.movement = movement;
        session.drive = drive;
        session.ros = ros;
        session.terrain = terrain;
        session.spawn = spawn;
        session.patchSize = info.size;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/glacier-build.json", JsonUtility.ToJson(info, true));
        Debug.Log("Glacier scene created: real-scale terrain, original CAD chassis, four driven wheels.");
    }

    static Terrain MakeTerrain(string name, int resolution, float size, float height, Vector3 position)
    {
        string texturePath = TerrainRoot + "Data/color.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 4096;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 8;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var data = new TerrainData
        {
            heightmapResolution = resolution,
            size = new Vector3(size, height, size),
            alphamapResolution = 16,
            baseMapResolution = 2048
        };
        byte[] bytes = File.ReadAllBytes(TerrainRoot + "Data/heights.bytes");
        if (bytes.Length != resolution * resolution * 2)
            throw new InvalidDataException("Height grid dimensions do not match metadata");
        var heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = (z * resolution + x) * 2;
                heights[z, x] = (bytes[i] + bytes[i + 1] * 256) / 65535f;
            }
        data.SetHeights(0, 0, heights);
        var layer = new TerrainLayer
        {
            diffuseTexture = texture,
            tileSize = new Vector2(size, size),
            smoothness = .08f,
            metallic = 0
        };
        SaveAsset(layer, "Surface.terrainlayer");
        data.terrainLayers = new[] { layer };
        var alpha = new float[16, 16, 1];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                alpha[y, x, 0] = 1;
        data.SetAlphamaps(0, 0, alpha);
        SaveAsset(data, "Terrain.asset");
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = name;
        go.transform.position = position;
        var terrain = go.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.basemapDistance = 1500;
        var material = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
        SaveAsset(material, "Terrain.mat");
        terrain.materialTemplate = material;
        return terrain;
    }

    static Material MaterialAsset(string name, Color color, float metallic, float smoothness)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        SaveAsset(mat, name + ".mat");
        return mat;
    }

    static void SaveAsset(UnityEngine.Object obj, string name)
    {
        string path = (obj is Material ? MaterialsRoot : TerrainRoot) + name;
        // These assets are produced only by this explicit scene builder.
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
    }

    static void Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 size, Quaternion rotation, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.transform.localRotation = rotation;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
