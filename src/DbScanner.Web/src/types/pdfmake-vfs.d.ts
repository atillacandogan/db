declare module "pdfmake/build/vfs_fonts" {
  import type { TVirtualFileSystem } from "pdfmake/interfaces";

  const virtualFileSystem: TVirtualFileSystem;
  export default virtualFileSystem;
}
