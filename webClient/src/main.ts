import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { subscribeToPayload } from "./fetchModel";
import type { RhinoPayload, Floor, Wall, Beam, Column, Point3D } from "./types";

import { createSpeechRecognizer } from "./speech";
import { parseAndExecute } from "./commands";
// import { sendToFirebase } from "./sender";

// Renderer
const container = document.getElementById("scene-container") as HTMLDivElement;
const scene = new THREE.Scene();
scene.background = new THREE.Color(0xffffff);

const camera = new THREE.PerspectiveCamera(
    60,
    container.clientWidth / container.clientHeight,
    0.1,
    2000,
);
camera.position.set(25, 60, 80);
camera.lookAt(25, 0, 25);
camera.up.set(0, 1, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(container.clientWidth, container.clientHeight);
renderer.setPixelRatio(window.devicePixelRatio);
container.appendChild(renderer.domElement);

// Controls
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.target.set(25, 0, 25);

scene.add(new THREE.AxesHelper(100));

// Lights
scene.add(new THREE.AmbientLight(0xffffff, 0.6));
const dirLight = new THREE.DirectionalLight(0xffffff, 1);
dirLight.position.set(50, 80, 50);
scene.add(dirLight);

// Grid
// const grid = new THREE.GridHelper(200, 40, 0x444444, 0x333333);
// scene.add(grid);

// Model group — all built meshes live here so clearModel() works
const modelGroup = new THREE.Group();
scene.add(modelGroup);

function clearModel() {
    // Dispose geometry and materials before clearing
    modelGroup.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
            obj.geometry.dispose();
        }
    });
    modelGroup.clear();
}

// Materials
const MAT = {
    floor: new THREE.MeshStandardMaterial({
        color: 0x8ecae6,
        transparent: true,
        opacity: 0.6,
        side: THREE.DoubleSide,
    }),
    wall: new THREE.MeshStandardMaterial({ color: 0xe0e0e0 }),
    beam: new THREE.MeshStandardMaterial({ color: 0xf4a261 }),
    column: new THREE.MeshStandardMaterial({ color: 0x2a9d8f }),
};

// Helpers
function v3(p: Point3D) {
    return new THREE.Vector3(p.x, p.z, -p.y);
}

// Builders — all use modelGroup.add
function buildFloor(floor: Floor) {
    const shape = new THREE.Shape(
        floor.polyline.map((p) => new THREE.Vector2(p.x, -p.y)),
    );

    const geo = new THREE.ExtrudeGeometry(shape, {
        depth: floor.thickness,
        bevelEnabled: false,
    });

    const mesh = new THREE.Mesh(geo, MAT.floor);
    mesh.rotation.x = Math.PI / 2;
    mesh.position.y = floor.polyline[0].z; // elevation from z

    modelGroup.add(mesh);
}

function buildWall(wall: Wall) {
    const p0 = wall.polyline[0];
    const p1 = wall.polyline[1];
    const h = wall.height;
    const t = wall.thickness;

    // Direction in XY (Rhino plan)
    const dx = p1.x - p0.x;
    const dy = p1.y - p0.y;
    const len = Math.sqrt(dx * dx + dy * dy);
    const nx = -dy / len; // perpendicular
    const ny = dx / len;

    // 4 corners in Rhino XY, offset by thickness
    const ax = p0.x;
    const ay = p0.y;
    const bx = p1.x;
    const by = p1.y;
    const cx = p1.x + nx * t;
    const cy = p1.y + ny * t;
    const dx2 = p0.x + nx * t;
    const dy2 = p0.y + ny * t;

    const shape = new THREE.Shape();
    shape.moveTo(ax, ay);
    shape.lineTo(bx, by);
    shape.lineTo(cx, cy);
    shape.lineTo(dx2, dy2);
    shape.closePath();

    const geo = new THREE.ExtrudeGeometry(shape, {
        depth: h,
        bevelEnabled: false,
    });

    const mesh = new THREE.Mesh(geo, MAT.wall);

    // Lay flat in XY then rotate up — extrude goes along Z → rotate to Y up
    mesh.rotation.x = -Math.PI / 2;
    mesh.position.y = p0.z; // elevation

    modelGroup.add(mesh);
}

function buildBeam(beam: Beam) {
    const start = v3(beam.line[0]);
    const end = v3(beam.line[1]);
    const length = start.distanceTo(end);

    const w = beam.xandy.b;
    const d = beam.xandy.h;

    const shape = new THREE.Shape();
    shape.moveTo(-w / 2, -d / 2);
    shape.lineTo(w / 2, -d / 2);
    shape.lineTo(w / 2, d / 2);
    shape.lineTo(-w / 2, d / 2);
    shape.closePath();

    const geo = new THREE.ExtrudeGeometry(shape, {
        depth: length,
        bevelEnabled: false,
    });

    const mesh = new THREE.Mesh(geo, MAT.beam);
    mesh.position.copy(start);

    const dir = end.clone().sub(start).normalize();
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), dir);

    modelGroup.add(mesh);
}

function buildColumn(col: Column) {
    const start = v3(col.line[0]);
    const end = v3(col.line[1]);
    const length = start.distanceTo(end);

    const w = col.xandy.b;
    const d = col.xandy.h;

    const shape = new THREE.Shape();
    shape.moveTo(-w / 2, -d / 2);
    shape.lineTo(w / 2, -d / 2);
    shape.lineTo(w / 2, d / 2);
    shape.lineTo(-w / 2, d / 2);
    shape.closePath();

    const geo = new THREE.ExtrudeGeometry(shape, {
        depth: length,
        bevelEnabled: false,
    });

    const mesh = new THREE.Mesh(geo, MAT.column);
    mesh.position.copy(start);

    const dir = end.clone().sub(start).normalize();
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), dir);

    modelGroup.add(mesh);
}

function renderPayload(payload: RhinoPayload) {
    clearModel();
    payload.floors?.forEach(buildFloor);
    payload.walls?.forEach(buildWall);
    payload.beams?.forEach(buildBeam);
    payload.columns?.forEach(buildColumn);

    const status = document.getElementById("status") as HTMLParagraphElement;
    status.textContent = `Loaded: ${payload.documentName ?? "model"}`;
    status.style.color = "gray";
}

// Subscribe — rebuilds scene automatically on Firestore changes
const unsubscribe = subscribeToPayload(renderPayload, (err) => {
    console.error(err);
    const status = document.getElementById("status") as HTMLParagraphElement;
    status.textContent = "Failed to load model";
    status.style.color = "red";
});

// Resize
window.addEventListener("resize", () => {
    camera.aspect = container.clientWidth / container.clientHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(container.clientWidth, container.clientHeight);
});

// Loop
function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);
}

animate();

const startBtn = document.getElementById("startBtn") as HTMLButtonElement;
const stopBtn = document.getElementById("stopBtn") as HTMLButtonElement;
const status = document.getElementById("status") as HTMLParagraphElement;
const output = document.getElementById("output") as HTMLTextAreaElement;

let lastTranscript = "";
let commandCooldown = false;

const recognition = createSpeechRecognizer(async (text) => {
    output.value = text;

    // Don't fire if same transcript or cooldown active
    if (text === lastTranscript || commandCooldown) return;
    lastTranscript = text;

    // Set cooldown to prevent duplicate fires
    commandCooldown = true;
    setTimeout(() => {
        commandCooldown = false;
        lastTranscript = ""; // reset so next command can fire
    }, 300); // 2 second cooldown between commands

    await parseAndExecute(
        text,
        (cmd: any) => {
            status.textContent = `Running: "${cmd}"...`;
            status.style.color = "orange";
        },
        (cmd: any) => {
            status.textContent = `✅ Done: "${cmd}"`;
            status.style.color = "lightgreen";
        },
        (err: any) => {
            status.textContent = `❌ ${err.message}`;
            status.style.color = "red";
        },
    );
});

startBtn.addEventListener("click", () => {
    lastTranscript = "";
    recognition.start(); // now calls the wrapper
    startBtn.disabled = true;
    stopBtn.disabled = false;
    status.textContent = "Listening...";
    status.style.color = "red";
});

stopBtn.addEventListener("click", () => {
    recognition.stop(); // sets active = false, won't restart
    startBtn.disabled = false;
    stopBtn.disabled = true;
    status.textContent = "Idle";
    status.style.color = "gray";
});
