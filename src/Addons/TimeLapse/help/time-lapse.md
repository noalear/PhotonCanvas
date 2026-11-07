# Time-lapse

This add-on adds **Time-lapse** to the Machine tab's **Job** group: a switch that films your jobs with the overview camera.

## Time-lapse

**Machine → Time-lapse** (a switch, off to begin with) takes an overview camera photo every 5
seconds while a job runs, and makes a video of them when the job ends. To change how often, set
`TimeLapseSeconds` in `app.json` (1–120 seconds); there's no setting for it in the program.

- It starts when the job starts running, if the switch is on then.
- The photos go in a folder named after the date, time and design in your Pictures folder, under
  *PhotonCanvas time-lapses*.
- At the end, if **ffmpeg** is installed (on the PATH), they become an .mp4 video at 10 photos a
  second and the photo folder is removed. Without ffmpeg the photos are kept, and the caption says
  *Time-lapse photos saved in … (install ffmpeg to get a video).*
- PhotonCanvas doesn't come with ffmpeg; it's a free download.
- The machine's lights are off during a job, so the photos are dark.

Not yet checked on the machine that taking photos doesn't disturb a running job.

## Tutorials

- [Make a time-lapse](tutorial:job-time-lapse)
