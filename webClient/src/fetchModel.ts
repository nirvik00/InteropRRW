import { db } from "./firebase";
import { doc, onSnapshot } from "firebase/firestore";
import { firebaseCollection, firebaseDoc } from "./firebaseConfigFile";
import type { RhinoPayload } from "./types";

export function subscribeToPayload(
    onUpdate: (payload: RhinoPayload) => void,
    onError?: (err: Error) => void,
) {
    const ref = doc(db, firebaseCollection, firebaseDoc);

    const unsubscribe = onSnapshot(
        ref,
        (snap) => {
            if (!snap.exists()) {
                onError?.(new Error("Document not found"));
                return;
            }
            onUpdate(snap.data() as RhinoPayload);
        },
        (err) => onError?.(err),
    );

    return unsubscribe;
}
