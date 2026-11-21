import React from "react";
import {
  AbsoluteFill,
  Easing,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
export const pink = "#ff458c",
  orange = "#ffb36b",
  cyan = "#78edec";
export const clamp = {
  extrapolateLeft: "clamp",
  extrapolateRight: "clamp",
} as const;
export const Label: React.FC<{ children: React.ReactNode; color?: string }> = ({
  children,
  color = orange,
}) => (
  <div
    style={{
      fontSize: 30,
      fontWeight: 700,
      letterSpacing: 6,
      color,
      textTransform: "uppercase",
    }}
  >
    {children}
  </div>
);
export const Head: React.FC<{
  children: React.ReactNode;
  size?: number;
  color?: string;
}> = ({ children, size = 112, color = "white" }) => {
  const f = useCurrentFrame(),
    { fps } = useVideoConfig();
  return (
    <div
      style={{
        fontFamily: "Impact, Arial Black, sans-serif",
        fontSize: size,
        lineHeight: 0.98,
        letterSpacing: -2,
        color,
        scale: interpolate(
          spring({ frame: f, fps, config: { damping: 16, stiffness: 160 } }),
          [0, 1],
          [1.2, 1],
        ),
        textShadow: "0 5px 30px #0008",
      }}
    >
      {children}
    </div>
  );
};
export const Stage: React.FC<{
  children: React.ReactNode;
  chapter: string;
  note?: string;
}> = ({ children, chapter, note }) => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill
      style={{
        color: "white",
        fontFamily: "Arial, sans-serif",
        background: "#090813",
        overflow: "hidden",
      }}
    >
      <City />
      <AbsoluteFill
        style={{
          background:
            "linear-gradient(180deg,#09081310,#09081355 35%,#090813ef 88%)",
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 80,
          right: 80,
          top: 116,
          display: "flex",
          justifyContent: "space-between",
          fontSize: 26,
          letterSpacing: 4,
          color: "#ffffffaa",
        }}
      >
        <span>THE LAUNCH FILES</span>
        <span>{chapter}</span>
      </div>
      <div
        style={{
          position: "absolute",
          top: 180,
          left: 80,
          width: 920,
          height: 3,
          background: "#ffffff25",
        }}
      >
        <div
          style={{ width: 90 + (f % 300) * 2, height: 3, background: pink }}
        />
      </div>
      {children}
      <div
        style={{
          position: "absolute",
          bottom: 128,
          left: 80,
          right: 100,
          color: "#b9b4c6",
          fontSize: 25,
          lineHeight: 1.4,
        }}
      >
        {note}
      </div>
      <AbsoluteFill
        style={{
          pointerEvents: "none",
          background:
            "repeating-linear-gradient(0deg,transparent,transparent 4px,#00000016 5px)",
          opacity: 0.3,
        }}
      />
    </AbsoluteFill>
  );
};
export const City = () => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill
      style={{
        scale: 1.06 + f * 0.00015,
        translate: `${Math.sin(f / 70) * 12}px 0px`,
      }}
    >
      <svg width="1080" height="1920" viewBox="0 0 1080 1920">
        <defs>
          <linearGradient id="sky" x2="0" y2="1">
            <stop stopColor="#180e39" />
            <stop offset=".46" stopColor="#df438d" />
            <stop offset=".7" stopColor="#ffac75" />
            <stop offset="1" stopColor="#110c23" />
          </linearGradient>
          <linearGradient id="sun" x2="0" y2="1">
            <stop stopColor="#ffe8a6" />
            <stop offset="1" stopColor="#ff4c8b" />
          </linearGradient>
        </defs>
        <rect width="1080" height="1920" fill="url(#sky)" />
        <circle cx="650" cy="650" r="255" fill="url(#sun)" />
        {Array.from({ length: 10 }, (_, i) => (
          <rect
            key={i}
            x="380"
            y={700 + i * 19}
            width="540"
            height={3 + i * 1.4}
            fill="#9a326c"
          />
        ))}
        {[0, 1, 2].map((layer) => (
          <g
            key={layer}
            fill={["#502047", "#28132f", "#100e21"][layer]}
            style={{
              translate: `${Math.sin(f / 170) * 10 * (layer + 1)}px 0px`,
            }}
          >
            {Array.from({ length: 16 }, (_, i) => {
              const h = 100 + ((i * 97 + layer * 113) % 330);
              return (
                <g key={i}>
                  <rect
                    x={i * 85 - 90}
                    y={1060 - h + layer * 70}
                    width={65 + layer * 10}
                    height={h + 350}
                  />
                  {Array.from({ length: 6 }, (_, j) => (
                    <rect
                      key={j}
                      x={i * 85 - 78}
                      y={1080 - h + layer * 70 + j * 38}
                      width="30"
                      height="5"
                      fill={j % 2 ? pink : orange}
                      opacity=".4"
                    />
                  ))}
                </g>
              );
            })}
          </g>
        ))}
        <path d="M450 1150L-250 1920H1330L630 1150Z" fill="#090a16" />
        {Array.from({ length: 12 }, (_, i) => {
          const p = ((i / 12 + f / 320) % 1) ** 2;
          return (
            <path
              key={i}
              d={`M${450 - p * 700} ${1150 + p * 770}H${630 + p * 700}`}
              stroke={pink}
              opacity={0.1 + p * 0.4}
              strokeWidth={2 + p * 5}
            />
          );
        })}
        <path
          d="M450 1150L-250 1920M630 1150L1330 1920"
          stroke={pink}
          strokeWidth="8"
        />
        {[160, 920].map((x, i) => (
          <g
            key={x}
            transform={`translate(${x} 870) rotate(${i ? -9 : 9})`}
            fill="#0a0b19"
          >
            <path d="M0 500Q40 220 0 0H20Q80 220 32 500Z" />
            {[-80, -45, 0, 45, 80].map((a) => (
              <path
                key={a}
                transform={`rotate(${a})`}
                d="M0 0Q-100 -95 -220 -25Q-80 -80 0 15Q80 -90 210 -20Q110 -115 0 0"
              />
            ))}
          </g>
        ))}
      </svg>
    </AbsoluteFill>
  );
};
export const Panel: React.FC<{ children: React.ReactNode; top?: number }> = ({
  children,
  top = 330,
}) => {
  const f = useCurrentFrame();
  return (
    <div
      style={{
        position: "absolute",
        left: 80,
        right: 100,
        top,
        translate: `0px ${interpolate(f, [0, 22], [80, 0], { ...clamp, easing: Easing.out(Easing.cubic) })}px`,
      }}
    >
      {children}
    </div>
  );
};
export const Flash = () => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill
      style={{
        background: "#fff2e3",
        opacity: interpolate(f, [0, 3, 9], [0.65, 0.15, 0], clamp),
        pointerEvents: "none",
      }}
    />
  );
};
