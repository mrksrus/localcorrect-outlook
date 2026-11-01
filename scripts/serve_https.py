"""Loopback-only development HTTPS. No trust-store edits or request logging."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import ssl


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cert', required=True, type=Path)
    parser.add_argument('--key', required=True, type=Path)
    parser.add_argument('--directory', default='build/site', type=Path)
    parser.add_argument('--port', default=3000, type=int)
    args = parser.parse_args()
    directory = args.directory.resolve(strict=True)
    if not directory.is_dir():
        parser.error('--directory must be a directory')

    class Handler(SimpleHTTPRequestHandler):
        def log_message(self, *_args):
            pass  # No URL/header/content logging.

        def end_headers(self):
            self.send_header('Referrer-Policy', 'no-referrer')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Cache-Control', 'no-store')
            super().end_headers()

        def list_directory(self, path):
            self.send_error(404)
            return None

        def send_head(self):
            # Protect both GET and HEAD from symlinks outside the asset root.
            candidate = Path(self.translate_path(self.path)).resolve()
            if not candidate.is_relative_to(directory):
                self.send_error(403)
                return None
            return super().send_head()

    server = ThreadingHTTPServer(('127.0.0.1', args.port), partial(Handler, directory=str(directory)))
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(args.cert, args.key)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print(f'Serving HTTPS on https://localhost:{server.server_port} (loopback only); Ctrl-C to stop.', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
