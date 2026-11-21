import {
  AbsoluteFill,
  Audio,
  Composition,
  Folder,
  Sequence,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
  spring,
} from "remotion";
import { Hook } from "./GTA6/Hook";
import { Revenue } from "./GTA6/Revenue";
import { Preorders } from "./GTA6/Preorders";
import { Minute } from "./GTA6/Minute";
import { Ending } from "./GTA6/Ending";
import captions from "./GTA6/captions.json";
import voiceCaptions from "./GTA6/voice-captions.json";
import timing from "./GTA6/voice-timing.json";
import { Flash, pink } from "./GTA6/Design";
type Props = { voiceoverFile: string; mixedAudioFile: string };
const Subtitles = ({ voiced }: { voiced: boolean }) => {
  const f = useCurrentFrame(),
    { fps } = useVideoConfig();
  const time = (f / fps) * 1000;
  const cap = (voiced ? voiceCaptions : captions).find(
    (c) => time >= c.startMs && time < c.endMs,
  );
  if (!cap || time >= (timing.black / fps) * 1000) return null;
  const words = cap.text.split(" ");
  const measuredWords =
    voiced && "words" in cap
      ? (cap.words as (typeof voiceCaptions)[number]["words"])
      : [];
  return (
    <div
      style={{
        position: "absolute",
        left: 80,
        right: 100,
        bottom: 245,
        textAlign: "center",
        fontFamily: "Arial",
        fontSize: 48,
        fontWeight: 900,
        lineHeight: 1.24,
        textShadow: "0 4px 12px #000",
        background: "#080711eb",
        padding: "16px 20px",
        borderRadius: 8,
        scale:
          0.96 +
          0.04 *
            spring({
              frame: f - (cap.startMs / 1000) * fps,
              fps,
              config: { damping: 20 },
            }),
      }}
    >
      {words.map((word, i) => (
        <span
          key={i}
          style={{
            color:
              measuredWords[i] &&
              time >= measuredWords[i].startMs &&
              time < measuredWords[i].endMs
                ? pink
                : /million|billion|GTA|Avatar/i.test(word)
                  ? "#ffb36b"
                  : "white",
          }}
        >
          {word}
          {i < words.length - 1 ? " " : ""}
        </span>
      ))}
    </div>
  );
};
export const GTA6RecordShort = ({ voiceoverFile, mixedAudioFile }: Props) => {
  const { fps } = useVideoConfig();
  return (
    <AbsoluteFill style={{ background: "#06050b" }}>
      <Sequence
        name="01 Hook"
        durationInFrames={timing.revenue}
        premountFor={fps}
      >
        <Hook />
        <Flash />
      </Sequence>
      <Sequence
        name="02 Estimated retail sales"
        from={timing.revenue}
        durationInFrames={timing.preorders - timing.revenue}
        premountFor={fps}
      >
        <Revenue />
        <Flash />
      </Sequence>
      <Sequence
        name="03 Pre-release and sales race"
        from={timing.preorders}
        durationInFrames={timing.minute - timing.preorders}
        premountFor={fps}
      >
        <Preorders raceAfterFrame={timing.race - timing.preorders} />
        <Flash />
      </Sequence>
      <Sequence
        name="04 Money per minute"
        from={timing.minute}
        durationInFrames={timing.ending - timing.minute}
        premountFor={fps}
      >
        <Minute />
        <Flash />
      </Sequence>
      <Sequence
        name="05 The next impossible number"
        from={timing.ending}
        durationInFrames={60 * fps - timing.ending}
        premountFor={fps}
      >
        <Ending blackAfterFrame={timing.black - timing.ending} />
        <Flash />
      </Sequence>
      <Subtitles
        voiced={Boolean(voiceoverFile?.trim() || mixedAudioFile?.trim())}
      />
      {mixedAudioFile?.trim() ? (
        <Audio src={staticFile(mixedAudioFile)} />
      ) : (
        <Audio
          src={staticFile("gta6/score.wav")}
          volume={voiceoverFile?.trim() ? 0.32 : 0.8}
        />
      )}
      {!mixedAudioFile?.trim() && voiceoverFile?.trim() ? (
        <Audio src={staticFile(voiceoverFile)} volume={1} />
      ) : null}
    </AbsoluteFill>
  );
};
export const GTA6RecordCompositions = () => (
  <>
    <Composition
      id="GTA6RecordShort"
      component={GTA6RecordShort}
      width={1080}
      height={1920}
      fps={30}
      durationInFrames={1800}
      defaultProps={{
        voiceoverFile: "gta6/voiceover.wav",
        mixedAudioFile: "gta6/voiced-mix.wav",
      }}
    />
    <Folder name="GTA6-Scenes">
      <Composition
        id="GTA6-Hook"
        component={Hook}
        width={1080}
        height={1920}
        fps={30}
        durationInFrames={333}
      />
      <Composition
        id="GTA6-Revenue"
        component={Revenue}
        width={1080}
        height={1920}
        fps={30}
        durationInFrames={397}
      />
      <Composition
        id="GTA6-Preorders"
        component={Preorders}
        width={1080}
        height={1920}
        fps={30}
        durationInFrames={361}
        defaultProps={{ raceAfterFrame: 168 }}
      />
      <Composition
        id="GTA6-Minute"
        component={Minute}
        width={1080}
        height={1920}
        fps={30}
        durationInFrames={349}
      />
      <Composition
        id="GTA6-Ending"
        component={Ending}
        width={1080}
        height={1920}
        fps={30}
        durationInFrames={360}
        defaultProps={{ blackAfterFrame: 224 }}
      />
    </Folder>
  </>
);
