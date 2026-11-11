# Halation Unity simulation

Unity simulation of the Halation rover driving over a surveyed patch of Russell Glacier, Greenland.
The project includes the scene, terrain, rover chassis, wheel physics, and keyboard/ROS controls.

## Open and run

1. Clone this repository:

   ```sh
   git clone https://github.com/stanford-ssi/halation-unity-simulation.git
   ```

2. Install Unity **6000.6.4f1** through Unity Hub.
3. In Unity Hub, add the cloned repository folder and open it.
4. Open `Assets/Scenes/RussellGlacier.unity`, or choose **Halation > Open glacier scene**.
5. Press **Play** and click inside the Game view.

The saved scene and terrain are included; no terrain download or scene regeneration is needed.
Keyboard driving works without Docker or ROS.
Unity recreates its local cache on first launch, which can take a few minutes.

## Controls

| Control | Action |
| --- | --- |
| W / S | Forward / reverse |
| A / D | Skid steering |
| Space | Brake |
| R | Reset the rover |
| V | Switch follow and overview cameras |
| Tab | Switch keyboard and ROS control |

Select **Rover > Rover Physics Drive > Maximum Speed** to change the target wheel speed in metres per second.
The **Grip** field selects firm-ground or ice tire friction; terrain type is not detected automatically.
Change settings outside Play mode and save the scene to keep them.
Leaving the terrain patch resets the rover to its starting point.

## ROS control

The companion rover and web-controller repository is [stanford-ssi/halation](https://github.com/stanford-ssi/halation).
ROS control requires a running ROS 2 rosbridge server; this Unity repository does not include the bridge or web app.
The simulation bridge and web controller are included on its default `master` branch.
Follow the [local simulation setup](https://github.com/stanford-ssi/halation/blob/master/docker/simulation/README.md) or the quick start below.

### Teammate quick start

Install Git, Unity Hub with Unity **6000.6.4f1**, Docker Desktop (or Docker Engine with Compose), and Node.js **22 or newer** with npm.
Both repositories can be cloned without a GitHub account or an organization invitation.
The Unity scene, rover model, terrain, and required assets are included in this repository.
Each teammate runs Unity, the bridge, and the web controller on their own computer.

After cloning and opening this Unity project as described above, open a terminal in the folder where you keep your projects and run:

```sh
git clone https://github.com/stanford-ssi/halation.git halation-simulation
cd halation-simulation
docker compose -f docker-compose.sim.yaml up -d --build --wait
cd web
```

In the current `web` directory, copy `.env.example` to `.env.local` if it does not already exist.
Make sure `.env.local` contains both settings:

```dotenv
NEXT_PUBLIC_ROSBRIDGE_URL=ws://127.0.0.1:9095
NEXT_PUBLIC_SIMULATION_MODE=true
```

Simulation mode enables the command timing and connection handling used by Unity.
Without this flag, the web controller retains its standard rover behavior.
Then, from the `web` directory, run:

```sh
npm install
npm run dev -- --hostname 127.0.0.1
```

Open <http://localhost:3000> and confirm that the displayed bridge address is `ws://127.0.0.1:9095`.
In Unity, open `Assets/Scenes/RussellGlacier.unity`, press **Play**, click the Game view, and press **Tab** to select ROS control.
Use the browser joystick to drive and release it to brake.
The browser and Unity each connect to the local bridge; neither needs to connect to a teammate's computer or the physical rover.
To finish, stop the web server with Ctrl+C, exit Unity Play mode, and run `docker compose -f docker-compose.sim.yaml down` from the `halation-simulation` root.

If the browser shows a different bridge address or held commands stop after a second, check both settings in `.env.local` and restart the web server.
If it cannot connect, make sure Docker is running and inspect `docker compose -f docker-compose.sim.yaml logs --tail=50 rosbridge` from the companion repository root.
Keyboard controls are available immediately by switching Unity back with **Tab**, even without the bridge.

Set **Rover > Rover Ros Client > Bridge Url** to your rosbridge WebSocket address.
The default is `ws://127.0.0.1:9095`, for a bridge running on the same computer.
Press **Tab** in the Game view, or set **Rover Movement > Control Mode** to **Ros**.
Use **Halation > Inspect rover** to see connection status and received commands.

| Topic | ROS type | Message |
| --- | --- | --- |
| `/motor_vector` | `std_msgs/msg/String` | JSON with steering `x` and forward `y`, each from -1 to 1 |
| `/motor_estop` | `std_msgs/msg/String` | Any message applies the brakes |

For example, a `/motor_vector` String containing `{"x":0,"y":0.5}` requests forward motion at half the configured target speed.
The receiver also accepts the Halation web controller's nested JSON format.
Send driving commands repeatedly; commands expire after one second by default.
Disconnects, invalid numeric inputs, and E-stop messages clear the command and apply the brakes.
A new valid command can move the rover again after an E-stop.

## Project folders

- `Assets/Scenes`: saved glacier scene.
- `Assets/Scripts`: rover physics, controls, ROS client, camera, and reset behavior.
- `Assets/Editor`: scene builder and Inspector tools.
- `Assets/Terrain`: terrain asset, surface layer, height samples, aerial image, and crop metadata.
- `Assets/Materials`: chassis, tire, terrain, and sky materials.
- `Assets/Models`: rover chassis model.
- `Packages` and `ProjectSettings`: Unity dependencies and project configuration.
- `Tools`: optional terrain importer and its Python dependencies.

Keep Unity `.meta` files alongside their assets; they preserve scene and asset references.
Local caches, logs, editor preferences, and original full-size survey rasters are excluded from Git.

## Terrain and physics

The scene contains one **204.8 × 204.8 m** terrain patch with **2049 × 2049** elevation samples at **10 cm** spacing and no vertical exaggeration.
The aerial photograph is resampled to a 2048 × 2048 texture covering the same patch.
`Assets/Terrain/Data/terrain.json` records the source files, crop coordinates, and starting point.
Unity X points east, Z points north, and Y is elevation relative to 457 m in EPSG:32622.

The rover uses a dynamic Rigidbody, a chassis collider, and four driven WheelColliders with suspension and tire friction.
The default setup is a 100 kg prototype with 0.26 m wheel radius, 0.18 m suspension travel, and a 1.5 m/s target wheel speed.
Actual speed depends on terrain, traction, and motor torque.
These settings have not been calibrated to the physical rover.
The simulation does not model deformable ice, snow compaction, or a detailed suspension linkage.

## Optional terrain regeneration

Regeneration is only needed to choose a different crop or rebuild the scene.
Install `Tools/terrain-requirements.txt` in a Python 3.10+ environment and run `Tools/import_russell_glacier.py`.
The importer downloads the source data as needed; a complete DEM can optionally be supplied with `--dem`.
The full survey files are large and stay in the ignored `.terrain-cache` directory.

After importing, close the editor and run the Unity executable with:

```sh
Unity -batchmode -projectPath <project-folder> -executeMethod GlacierSceneBuilder.Build -quit
```

Use the Unity executable path for your installation in place of `Unity`.
This replaces the saved scene and builder-owned terrain and material assets, so commit or back up any manual scene edits first.

## Dataset attribution

Dømgaard, Mads; Bjørk, Anders Anker; Hubian, Flora (2023): UAV derived orthomosaics and digital elevation models of drained ice-dammed lake basin topography and ice margin, Russell Glacier, West Greenland, 3-6 Sept. 2021 [dataset].
PANGAEA, https://doi.org/10.1594/PANGAEA.955957.
Licensed under [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).

This project uses a cropped and converted Mission I elevation model and a cropped, resampled orthomosaic from that dataset.
The survey was collected in September 2021; this is a Greenland test environment.
The 10 cm spacing is sampling resolution, not survey accuracy; the dataset reports estimated vertical uncertainty of 0.53 m.
