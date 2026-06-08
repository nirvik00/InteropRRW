import { db } from "./firebase";
import { doc, setDoc, getDoc, serverTimestamp } from "firebase/firestore";
import { firebaseCollection, firebaseDoc } from "./firebaseConfigFile";
import type { RhinoPayload } from "./types";

const REF = () => doc(db, firebaseCollection, firebaseDoc);

export interface SimpleFloor {
    x: number;
    y: number;
    width: number;
    depth: number;
    elevation: number;
    thickness: number;
}

export interface SimpleWall {
    x0: number;
    y0: number;
    x1: number;
    y1: number;
    height: number;
    thickness: number;
}

async function getCurrentPayload(): Promise<RhinoPayload> {
    const snap = await getDoc(REF());
    if (snap.exists()) return snap.data() as RhinoPayload;
    return { floors: [], walls: [], beams: [], columns: [] } as any;
}

export async function addFloor(floor: SimpleFloor) {
    const current = await getCurrentPayload();

    const newFloor = {
        type: "floor",
        material: "concrete",
        id: crypto.randomUUID(),
        thickness: floor.thickness,
        polyline: [
            { x: floor.x, y: floor.y, z: floor.elevation },
            { x: floor.x, y: floor.y + floor.depth, z: floor.elevation },
            {
                x: floor.x + floor.width,
                y: floor.y + floor.depth,
                z: floor.elevation,
            },
            { x: floor.x + floor.width, y: floor.y, z: floor.elevation },
            { x: floor.x, y: floor.y, z: floor.elevation },
        ],
    };

    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        floors: [...(current.floors ?? []), newFloor],
    });
}

export async function addWalls(
    floorX: number,
    floorY: number,
    floorWidth: number,
    floorDepth: number,
    height: number,
    thickness: number,
) {
    const current = await getCurrentPayload();

    const makeWall = (x0: number, y0: number, x1: number, y1: number) => ({
        type: "wall",
        material: "concrete",
        id: crypto.randomUUID(),
        thickness,
        height,
        polyline: [
            { x: x0, y: y0, z: 0 },
            { x: x1, y: y1, z: 0 },
        ],
    });

    const walls = [
        makeWall(floorX, floorY, floorX + floorWidth, floorY), // bottom
        makeWall(
            floorX + floorWidth,
            floorY,
            floorX + floorWidth,
            floorY + floorDepth,
        ), // right
        makeWall(
            floorX + floorWidth,
            floorY + floorDepth,
            floorX,
            floorY + floorDepth,
        ), // top
        makeWall(floorX, floorY + floorDepth, floorX, floorY), // left
    ];

    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        walls: [...(current.walls ?? []), ...walls],
    });
}

export async function clearModel() {
    await setDoc(REF(), {
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        floors: [],
        walls: [],
        beams: [],
        columns: [],
    });
}

export async function clearWalls() {
    const current = await getCurrentPayload();
    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        walls: [],
    });
}

export async function addColumns(
    floorX: number,
    floorY: number,
    floorWidth: number,
    floorDepth: number,
    colHeight: number,
    b: number,
    h: number,
    spacing: number = 10,
) {
    const current = await getCurrentPayload();

    const cols = [];

    // Generate points around the perimeter at given spacing
    const pts: { x: number; y: number }[] = [];

    // Bottom edge: left to right
    for (let x = floorX; x <= floorX + floorWidth; x += spacing) {
        pts.push({ x, y: floorY });
    }
    // Right edge: bottom to top
    for (let y = floorY + spacing; y <= floorY + floorDepth; y += spacing) {
        pts.push({ x: floorX + floorWidth, y });
    }
    // Top edge: right to left
    for (let x = floorX + floorWidth - spacing; x >= floorX; x -= spacing) {
        pts.push({ x, y: floorY + floorDepth });
    }
    // Left edge: top to bottom
    for (let y = floorY + floorDepth - spacing; y > floorY; y -= spacing) {
        pts.push({ x: floorX, y });
    }

    for (const pt of pts) {
        cols.push({
            type: "column",
            id: crypto.randomUUID(),
            xandy: { b, h },
            line: [
                { x: pt.x, y: pt.y, z: 0 },
                { x: pt.x, y: pt.y, z: colHeight },
            ],
        });
    }

    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        columns: [...(current.columns ?? []), ...cols],
    });
}

export async function clearColumns() {
    const current = await getCurrentPayload();
    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        columns: [],
    });
}

export async function clearFloors() {
    const current = await getCurrentPayload();
    await setDoc(REF(), {
        ...current,
        schemaVersion: "rhino-review-interop/0.2",
        updatedAtUtc: serverTimestamp(),
        floors: [],
    });
}

export async function addRawPayload(raw: any) {
    const current = await getCurrentPayload();
    await setDoc(REF(), {
        ...current,
        schemaVersion: raw.schemaVersion ?? "rhino-review-interop/0.1",
        documentName: raw.documentName,
        updatedAtUtc: serverTimestamp(),
        floors: [...(current.floors ?? []), ...(raw.floors ?? [])],
        walls: [...(current.walls ?? []), ...(raw.walls ?? [])],
        beams: [...(current.beams ?? []), ...(raw.beams ?? [])],
        columns: [...(current.columns ?? []), ...(raw.columns ?? [])],
    });
}
