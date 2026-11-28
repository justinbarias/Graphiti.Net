import type { NextConfig } from "next";

// The page loads nothing from another host. The text of an answer comes from a model and can hold the address of an
// image, so the browser gets a policy that blocks each image, font, media file, frame, and request to another host.
const policy = [
  "img-src 'self' data: blob:",
  "media-src 'self'",
  "font-src 'self' data:",
  "connect-src 'self'",
  "frame-src 'none'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
].join("; ");

const nextConfig: NextConfig = {
  // The badge of the Next.js dev server sits in the bottom left corner, over the attribution of the rail. It is off.
  devIndicators: false,
  async headers() {
    return [{ source: "/:path*", headers: [{ key: "Content-Security-Policy", value: policy }] }];
  },
};

export default nextConfig;
