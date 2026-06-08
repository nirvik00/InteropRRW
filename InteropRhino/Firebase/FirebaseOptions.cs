using System;
using System.Linq;

namespace InteropRhino.Firebase
{
    public sealed class FirebaseOptions
    {
        public string DatabaseUrl { get;  } = string.Empty;
        public string ApiKey { get; } = string.Empty;
        public string ProjectId { get; } = string.Empty;
        public string ModelId { get; } = "test-model";
        public string FirestoreCollection { get; } = "rhinoReview";
        public string FirestoreDocumentPath { get; } = string.Empty;
        public string ServiceAccountKeyPath { get; } = string.Empty;

        public void ValidateRealtimeDatabase()
        {
            if (string.IsNullOrWhiteSpace(DatabaseUrl))
            {
                throw new InvalidOperationException("Firebase config is missing databaseUrl.");
            }

            if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Firebase config is missing apiKey.");
            }

            if (string.IsNullOrWhiteSpace(ModelId))
            {
                throw new InvalidOperationException("Firebase config is missing modelId.");
            }
        }

        public void ValidateFirestore()
        {
            if (string.IsNullOrWhiteSpace(ModelId))
            {
                throw new InvalidOperationException("Firebase config is missing modelId.");
            }

            if (string.IsNullOrWhiteSpace(FirestoreCollection))
            {
                throw new InvalidOperationException("Firebase config is missing firestoreCollection.");
            }
        }
    }

}
