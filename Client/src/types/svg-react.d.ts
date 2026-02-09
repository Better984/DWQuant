declare module "*.svg?react" {
  import * as React from "react";

  const ReactComponent: React.ComponentType<React.SVGProps<SVGSVGElement>>;
  export default ReactComponent;
}
