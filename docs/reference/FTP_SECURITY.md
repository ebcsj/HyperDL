# FTP/FTPS status and security

FTP and FTPS are not implemented. HyperDL currently accepts only HTTP and
HTTPS URLs.

FTP credentials must not be placed in URLs or logs. If FTP support is added,
FTPS with strict certificate validation should be preferred; plain FTP exposes
credentials and data to network observers. Credentials must use an operating
system credential store rather than plaintext settings or the download
database.
