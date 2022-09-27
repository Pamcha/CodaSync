Coda Sync for Unity
===

An easy-to-use database system for your Unity Games based on Coda.io.
With Coda Sync for Unity, you create and edit all your game’s data in a collaborative environment designed for teams!

<<[Website](https://coda.io/@pamcha/coda-sync "Coda Sync Website") | [Documentation](https://coda.io/@pamcha/coda-sync/documentation-1 "Coda Sync Documentation") | [Tips and tricks](https://coda.io/@pamcha/coda-sync/tips-tricks-9 "Coda Sync Tips and tricks")>>

![Coda Sync Cover](https://imgur.com/rDYUp8K)

Coda Sync for Unity allows you to synchronize the tables you have created in a coda doc (on coda.io) in your Unity Project.
Your data are imported as Scriptable Objects by automatically generating a class and its instances, and ready to be used anywhere in your code or in the inspector.

You can sync again any time, whenever you have made a change to your data on your doc(s).

![1. Create and design your data](https://imgur.com/WMECWzs)
![2. Make the perfect database for your game](https://imgur.com/Njc1oZc)
![3. Setup your Unity project](https://imgur.com/3EY5bju)
![4. Sync your asset references in your tables](https://imgur.com/PPE9n4X)
![5. Generate and update your data anytime](https://imgur.com/gdIBu1n)

⚠️ Please note that you need to create a free account on [coda.io](https://coda.io) to create your docs. Coda.io offers a generous free tier that should cover most of your needs.

🙋Pamcha is not related to Coda. We use Coda as makers for several years and have built this asset using the Coda API. Coda provides us a great support for this asset.

## Installation
### Requirement
![](https://img.shields.io/badge/Unity%202021.x-supported-blue.svg)  

This project has a dependency to `com.unity.editorcoroutines` package that will be automatically installed if not already installed in your project.

### Using Git

Find the manifest.json file in the Packages folder of your project and add a line to `dependencies` field.

* `"com.pamcha.codasync": "https://github.com/Pamcha/CodaSync.git"`

