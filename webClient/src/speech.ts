const SpeechRecognition =
    (window as any).SpeechRecognition ||
    (window as any).webkitSpeechRecognition;

export function createSpeechRecognizer(onResult: (text: string) => void) {
    const recognition = new SpeechRecognition();

    recognition.lang = "en-US";
    recognition.continuous = true;
    recognition.interimResults = true;

    let active = false;
    let restarting = false;

    recognition.onresult = (event: any) => {
        for (let i = event.resultIndex; i < event.results.length; i++) {
            if (event.results[i].isFinal) {
                const transcript = event.results[i][0].transcript.trim();
                onResult(transcript);
            }
        }
    };

    recognition.onerror = (event: any) => {
        // These are expected — onend will handle restart
        if (event.error === "no-speech" || event.error === "aborted") return;
        console.error("Speech error:", event.error);
    };

    recognition.onend = () => {
        if (!active || restarting) return;

        // Delay restart to avoid abort loop
        restarting = true;
        setTimeout(() => {
            restarting = false;
            if (active) {
                try {
                    recognition.start();
                } catch {
                    // Already running
                }
            }
        }, 100);
    };

    return {
        start() {
            active = true;
            restarting = false;
            try {
                recognition.start();
            } catch {
                // Already running
            }
        },
        stop() {
            active = false;
            restarting = false;
            recognition.stop();
        },
    };
}
