namespace ResourceRouter.Core.Models;

public static class NativeCapabilityProviders
{
    public static class CloudAI
    {
        public const string Auto = "auto";
        public const string OpenAiCompatible = "openai-compatible";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            OpenAiCompatible,
            None
        };
    }

    public static class Ocr
    {
        public const string Auto = "auto";
        public const string TesseractCli = "tesseract-cli";
        public const string OpenAiCompatible = "openai-compatible";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            TesseractCli,
            OpenAiCompatible,
            None
        };
    }

    public static class AudioTranscription
    {
        public const string Auto = "auto";
        public const string WhisperCli = "whisper-cli";
        public const string OpenAiCompatible = "openai-compatible";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            WhisperCli,
            OpenAiCompatible,
            None
        };
    }

    public static class CloudSync
    {
        public const string Auto = "auto";
        public const string WebDav = "webdav";
        public const string S3 = "s3";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            WebDav,
            S3,
            None
        };
    }

    public static class Thumbnail
    {
        public const string Auto = "auto";
        public const string Shell = "shell";
        public const string ManagedIcon = "managed-icon";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            Shell,
            ManagedIcon,
            None
        };
    }

    public static class Remote
    {
        public const string Auto = "auto";
        public const string LocalMesh = "local-mesh";
        public const string Tailscale = "tailscale";
        public const string Relay = "relay";
        public const string None = "none";

        public static readonly string[] All =
        {
            Auto,
            LocalMesh,
            Tailscale,
            Relay,
            None
        };
    }
}