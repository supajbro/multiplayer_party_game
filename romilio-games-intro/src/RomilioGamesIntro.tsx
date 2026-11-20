import React from "react";
import {
  AbsoluteFill,
  Composition,
  Easing,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";

type LetterProps = {
  index: number;
  name: string;
  x: number;
  width: number;
  dot?: boolean;
};

const image = (name: string) => staticFile(`logo/${name}.png`);

const Letter: React.FC<LetterProps> = ({index, name, x, width, dot}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  // Rounded starts are 0, 4, 7, 11, 14, 18, 22 at 30 fps.
  const local = frame - Math.round(index * 0.12 * fps);
  const duration = Math.round((2 - 6 * 0.12) * fps);
  // Explicitly lock each letter at its destination when its entrance ends.
  const progress = local >= duration ? 1 : spring({
    frame: local,
    fps,
    durationInFrames: duration,
    config: {damping: 18, stiffness: 140, mass: 0.8},
  });

  return (
    <div style={{
      position: "absolute", left: x, top: 240, width, height: 380,
      transformOrigin: "50% 353px",
      opacity: interpolate(local, [0, 0.3 * fps], [0, 1], {
        extrapolateLeft: "clamp", extrapolateRight: "clamp",
      }),
      translate: `0 ${interpolate(progress, [0, 1], [22, 0])}px`,
      scale: interpolate(progress, [0, 1], [0.9, 1]),
    }}>
      {dot ? <>
        <Img src={image(`${name}-dot`)} style={{position: "absolute", top: -24, left: -24, width: width + 48, height: 193}} />
        <Img src={image(`${name}-stem`)} style={{position: "absolute", top: 121, left: -24, width: width + 48, height: 283}} />
      </> : <Img src={image(name)} style={{position: "absolute", left: -24, top: -24, width: width + 48, height: 428}} />}
    </div>
  );
};

/** Original raster artwork, animated at its source coordinates. No fonts required. */
export const RomilioGamesIntro: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();

  return (
    <AbsoluteFill style={{backgroundColor: "#06011b", overflow: "hidden"}}>
      <Img src={image("background")} style={{position: "absolute", width: 1920, height: 1080}} />
      <Letter index={0} name="r" x={0} width={475} />
      <Letter index={1} name="o-1" x={475} width={269} />
      <Letter index={2} name="m" x={744} width={366} />
      <Letter index={3} name="i-1" x={1110} width={130} dot />
      <Letter index={4} name="l" x={1240} width={130} />
      <Letter index={5} name="i-2" x={1370} width={125} dot />
      <Letter index={6} name="o-2" x={1495} width={425} />
      <AbsoluteFill style={{
        opacity: interpolate(frame, [2 * fps, 2.5 * fps], [0, 1], {
          extrapolateLeft: "clamp", extrapolateRight: "clamp",
          easing: Easing.bezier(0.42, 0, 0.58, 1),
        }),
      }}>
        <Img src={image("line-left")} style={{position: "absolute", left: -24, top: 596, width: 578, height: 248}} />
        <Img src={image("games")} style={{position: "absolute", left: 506, top: 596, width: 913, height: 248}} />
        <Img src={image("line-right")} style={{position: "absolute", left: 1371, top: 596, width: 573, height: 248}} />
      </AbsoluteFill>
      {/* Same pixels, eliminates sub-channel alpha rounding during the final hold.
          Kept mounted from frame zero so decoding never interrupts the hold. */}
      <Img src={image("reference")} style={{
        position: "absolute", width: 1920, height: 1080,
        opacity: frame >= 2.5 * fps ? 1 : 0,
      }} />
    </AbsoluteFill>
  );
};

export const RomilioGamesIntroComposition: React.FC = () => (
  <Composition
    id="RomilioGamesIntro"
    component={RomilioGamesIntro}
    width={1920}
    height={1080}
    fps={30}
    durationInFrames={120}
  />
);
