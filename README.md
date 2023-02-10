# Shoko Server
Shoko server is the core component of the Shoko suite and with it's client-server architecture which allows any program or plugin to access Shoko. You'll have access to your entire collection locally and over the internet with no additional work outside the initial configuration required.

# What Is Shoko?
Shoko is an anime cataloging program designed to automate the cataloging of your collection regardless of the size and amount of files you have. Unlike other anime cataloging programs which make you manually add your series or link the files to them, Shoko removes the tedious, time consuming and boring task of having to manually add every file and manually input the file information. You have better things to do with your time like actually watching the series in your collection so let Shoko handle all the heavy lifting.

# Learn More About Shoko
http://shokoanime.com/

# Learn More About Shoko Server
http://shokoanime.com/shoko-server/

# Notes About Fork
I adjusted the invalid character replacement so it only uses spaces.

Building the image:

```bash
git submodule update --init --recursive
docker buildx build . --platform linux/amd64 --build-arg version=4.1.2 --build-arg channel=dev --build-arg commit=0db91a3
```

Currently, this will be published to Docker Hub as stefandesu/shokoserver.
