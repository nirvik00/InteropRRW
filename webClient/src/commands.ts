import {
    addFloor,
    addWalls,
    addColumns,
    clearModel,
    clearWalls,
    clearFloors,
    clearColumns,
    addRawPayload,
} from "./sender";

function extractNumber(transcript: string, keyword: string): number | null {
    const regex = new RegExp(`${keyword}\\s+([\\d.]+)`);
    const match = transcript.match(regex);
    return match ? parseFloat(match[1]) : null;
}

// Normalize common mispronunciations before parsing
function normalizeTranscript(transcript: string): string {
    return transcript
        .replace(/\ad\b/g, "add")
        .replace(/\at\b/g, "add")
        .replace(/\bwith\b/g, "width")
        .replace(/\bwit\b/g, "width")
        .replace(/\bwide\b/g, "width")
        .replace(/\bdepths?\b/g, "depth")
        .replace(/\bdept\b/g, "depth")
        .replace(/\belevations?\b/g, "elevation")
        .replace(/\belevate\b/g, "elevation")
        .replace(/\bthicknesses?\b/g, "thickness")
        .replace(/\bthick\b/g, "thickness")
        .replace(/\bheight?\b/g, "height")
        .replace(/\bheights?\b/g, "height")
        .replace(/\bhigh\b/g, "height")
        .replace(/\bspacing\b/g, "spacing")
        .replace(/\bspace\b/g, "spacing")
        .replace(/\bspaced\b/g, "spacing")
        .replace(/\bto\b/g, "2");
}

export async function parseAndExecute(
    transcript: string,
    onStart: (cmd: string) => void,
    onDone: (cmd: string) => void,
    onError: (err: Error) => void,
) {
    // const lower = transcript.toLowerCase();
    const lower = normalizeTranscript(transcript.toLowerCase());

    // --- clear walls only ---
    if (lower.includes("clear walls") || lower.includes("remove walls")) {
        onStart("clear walls");
        try {
            await clearWalls();
            onDone("walls cleared");
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // --- clear columns only ---
    if (lower.includes("clear columns") || lower.includes("remove columns")) {
        onStart("clear columns");
        try {
            await clearColumns();
            onDone("columns cleared");
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // --- clear floors only ---
    if (
        lower.includes("clear floors") ||
        lower.includes("clear floor") ||
        lower.includes("remove floor")
    ) {
        onStart("clear floors");
        try {
            await clearFloors();
            onDone("floors cleared");
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // --- clear everything ---
    if (lower.includes("clear all") || lower.includes("reset all")) {
        onStart("clear all");
        try {
            await clearModel();
            onDone("all cleared");
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // --- floor ---
    if (
        lower.includes("add floor") ||
        lower.includes("add slab") ||
        lower.includes("add data")
    ) {
        const width = extractNumber(lower, "width") ?? 50;
        const depth = extractNumber(lower, "depth") ?? 50;
        const thickness = extractNumber(lower, "thickness") ?? 1;
        const elevation = extractNumber(lower, "elevation") ?? 0;

        onStart("add floor");
        try {
            await addFloor({ x: 0, y: 0, width, depth, elevation, thickness });
            onDone(`floor w:${width} d:${depth} t:${thickness} e:${elevation}`);
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // --- walls ---
    // if (lower.includes("send wall") || lower.includes("send walls")) {
    //     const height = extractNumber(lower, "height") ?? 10;
    //     const thickness = extractNumber(lower, "thickness") ?? 1;

    //     onStart("add walls");
    //     try {
    //         await addWalls(
    //             { x0: 0, y0: 0, x1: 50, y1: 0, height, thickness },
    //             { x0: 0, y0: 50, x1: 50, y1: 50, height, thickness },
    //         );
    //         onDone(`walls h:${height} t:${thickness}`);
    //     } catch (err) {
    //         onError(err as Error);
    //     }
    //     return;
    // }
    if (
        lower.includes("add wall") ||
        lower.includes("add walls") ||
        lower.includes("add walls") ||
        lower.includes("had wall") ||
        lower.includes("had walls") ||
        lower.includes("had walls")
    ) {
        const width = extractNumber(lower, "width") ?? 50;
        const depth = extractNumber(lower, "depth") ?? 50;
        const height = extractNumber(lower, "height") ?? 10;
        const thickness = extractNumber(lower, "thickness") ?? 1;

        onStart("add walls");
        try {
            await addWalls(0, 0, width, depth, height, thickness);
            onDone(`walls around ${width}x${depth} h:${height} t:${thickness}`);
        } catch (err) {
            onError(err as Error);
        }
        return;
    }
    // --- columns ---
    if (lower.includes("add column")) {
        const width = extractNumber(lower, "width") ?? 50;
        const depth = extractNumber(lower, "depth") ?? 50;
        const height = extractNumber(lower, "height") ?? 10;
        const b = extractNumber(lower, "b") ?? 2;
        const h = extractNumber(lower, "h") ?? 2;
        const spacing = extractNumber(lower, "spacing") ?? 10;

        onStart("add columns");
        try {
            await addColumns(0, 0, width, depth, height, b, h, spacing);
            onDone(
                `columns around ${width}x${depth} h:${height} b:${b} h:${h}`,
            );
        } catch (err) {
            onError(err as Error);
        }
        return;
    }

    // add the whole model
    if (lower.includes("add model") || lower.includes("load model")) {
        onStart("add model");
        try {
            await addRawPayload(MODEL_PAYLOAD);
            onDone("model loaded");
        } catch (err) {
            onError(err as Error);
        }
        return;
    }
}

const MODEL_PAYLOAD = {
    documentName: "6 chairs Rhino Doc",
    schemaVersion: "rhino-review-interop/0.2",
    beams: [
        {
            type: "beam",
            id: "4d285bd3-1b23-4f32-973e-b48bd3c45ea9",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 49, z: 40 },
                { x: 50, y: 49, z: 40 },
            ],
        },
        {
            type: "beam",
            id: "048141be-1b87-499b-8de4-e96af72ae620",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 49, z: 19 },
                { x: 50, y: 49, z: 19 },
            ],
        },
        {
            type: "beam",
            id: "f38cfe13-ddae-4869-b571-91178660fe6f",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 25, z: 40 },
                { x: 50, y: 25, z: 40 },
            ],
        },
        {
            type: "beam",
            id: "2333fd1c-cad8-4d28-bac0-018dfe21d583",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 1, z: 40 },
                { x: 50, y: 1, z: 40 },
            ],
        },
        {
            type: "beam",
            id: "d4832732-10b3-4a44-b109-d4b32e781275",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 25, z: 19 },
                { x: 50, y: 25, z: 19 },
            ],
        },
        {
            type: "beam",
            id: "abeb40e9-57b1-43e5-bd38-047328cef6d7",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 0, y: 1, z: 19 },
                { x: 50, y: 1, z: 19 },
            ],
        },
    ],
    columns: [
        {
            type: "column",
            id: "98e624c6-151c-4f92-be24-c133d1068a5b",
            xandy: { b: 0, h: 0 },
            line: [
                { x: 50, y: 50, z: 0 },
                { x: 50, y: 50, z: 0 },
            ],
        },
        {
            type: "column",
            id: "a3f79c0f-5afb-40db-9f3a-32167ddd9943",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 49, z: 21 },
                { x: 49, y: 49, z: 39 },
            ],
        },
        {
            type: "column",
            id: "3237c694-5bd1-4be9-9615-11972fd962a5",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 49, z: 0 },
                { x: 49, y: 49, z: 18 },
            ],
        },
        {
            type: "column",
            id: "a8d195dd-8aa1-42f6-a08c-d7e45ae920d0",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 49, z: 21 },
                { x: 1, y: 49, z: 39 },
            ],
        },
        {
            type: "column",
            id: "60c2218f-1780-449f-a4f7-4001c7d831be",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 49, z: 0 },
                { x: 1, y: 49, z: 18 },
            ],
        },
        {
            type: "column",
            id: "ec3179df-50fd-4593-8d50-585150911d75",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 25, z: 0 },
                { x: 1, y: 25, z: 18 },
            ],
        },
        {
            type: "column",
            id: "0db3cc55-697d-4378-bc94-e44cb7a654cd",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 25, z: 21 },
                { x: 1, y: 25, z: 39 },
            ],
        },
        {
            type: "column",
            id: "0cf194a6-03fe-42f9-aa99-faefb215cf03",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 25, z: 0 },
                { x: 49, y: 25, z: 18 },
            ],
        },
        {
            type: "column",
            id: "1f2f6f9e-0245-441d-91bd-4d0c4cc35e89",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 25, z: 21 },
                { x: 49, y: 25, z: 39 },
            ],
        },
        {
            type: "column",
            id: "c62fe180-b800-440d-8e54-de5c165b0526",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 1, z: 0 },
                { x: 1, y: 1, z: 18 },
            ],
        },
        {
            type: "column",
            id: "48615573-5efa-44ce-8571-b7313d5c5e1f",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 1, y: 1, z: 21 },
                { x: 1, y: 1, z: 39 },
            ],
        },
        {
            type: "column",
            id: "5215d200-9baf-4002-9833-2b583f53322a",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 1, z: 0 },
                { x: 49, y: 1, z: 18 },
            ],
        },
        {
            type: "column",
            id: "02bb1a34-3299-4af0-82f0-e5fa1fb0ef30",
            xandy: { b: 2, h: 2 },
            line: [
                { x: 49, y: 1, z: 21 },
                { x: 49, y: 1, z: 39 },
            ],
        },
    ],
    floors: [
        {
            type: "floor",
            material: "concrete",
            id: "f5a57ec9-c0d6-4142-af5b-3c6a7a227572",
            thickness: 1,
            polyline: [
                { x: 0, y: 50, z: -1 },
                { x: 0, y: 0, z: -1 },
                { x: 50, y: 0, z: -1 },
                { x: 50, y: 50, z: -1 },
                { x: 0, y: 50, z: -1 },
            ],
        },
        {
            type: "floor",
            material: "concrete",
            id: "2bcd07bc-722c-43b5-bb11-1aa7ba1dbb16",
            thickness: 1,
            polyline: [
                { x: 0, y: 0, z: 21 },
                { x: 0, y: 50, z: 21 },
                { x: 50, y: 50, z: 21 },
                { x: 50, y: 0, z: 21 },
                { x: 0, y: 0, z: 21 },
            ],
        },
        {
            type: "floor",
            material: "concrete",
            id: "02fa6c47-de27-4f98-b975-2c918d4d5688",
            thickness: 1,
            polyline: [
                { x: 0, y: 0, z: 42 },
                { x: 0, y: 57.83981, z: 42 },
                { x: 50, y: 57.83981, z: 42 },
                { x: 50, y: 0, z: 42 },
                { x: 0, y: 0, z: 42 },
            ],
        },
    ],
    walls: [
        {
            type: "wall",
            material: "concrete",
            id: "9455c2d8-9ae4-49d8-897b-139ca4482eb3",
            thickness: 1,
            height: 22,
            polyline: [
                { x: 0, y: -0.5, z: -1 },
                { x: 50, y: -0.5, z: -1 },
            ],
        },
    ],
};

// "add floor width 50 depth 25 thickness 2 elevation 1"       floor 50×25, t=2, z=1
// "add floor width 30 depth 30"                               floor 30×30, t=1, z=0 (defaults)
// "add walls height 15 thickness 2"                           2 walls h=15, t=2
// "add data"                                                  floor 50×50 with all defaults

// new commands
// "add floor"                                                adds a 50×50 floor at elevation 0
// "add floor width 30 depth 20 thickness 2 elevation 5"      adds floor with those exact dimensions
// "add slab"                                                 same as add floor
// "add data"                                                 same as add floor
// "add wall" / "add walls"                                  adds 2 walls, height 10, thickness 1
// "add walls height 15 thickness 2"                          adds 2 walls with those dimensions
// "clear" / "reset"                                           wipes everything from Firestore

// "add columns width 5 depth 5 height 15 b 2 h 2"            columns at corners + every 10 units around perimeter
// "add columns width 3 depth 3 height 10 b 2 h 2 spacing 5"  denser columns every 5 units
// "add columns height 15 b 3 h 3 width 30 depth 30"
