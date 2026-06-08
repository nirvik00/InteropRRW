export interface Point3D {
    x: number;
    y: number;
    z: number;
}

export interface Floor {
    id: string;
    type: "floor";
    polyline: Point3D[];
    thickness: number;
    material?: string;
}

export interface Wall {
    id: string;
    type: "wall";
    polyline: Point3D[]; // [start, end]
    thickness: number;
    height: number;
    material?: string;
}

export interface Beam {
    id: string;
    type: "beam";
    line: Point3D[]; // [start, end]
    xandy: { b: number; h: number };
}

export interface Column {
    id: string;
    type: "column";
    line: Point3D[]; // [bottom, top]
    xandy: { b: number; h: number };
}

export interface RhinoPayload {
    floors: Floor[];
    walls: Wall[];
    beams: Beam[];
    columns: Column[];
    schemaVersion: string;
    documentName?: string; // <- add this
    updatedAtUtc?: string;
}
