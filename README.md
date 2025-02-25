Coda Sync for Unity
===

An easy-to-use database system for your Unity Games based on Coda.io.
With Coda Sync for Unity, you create and edit all your game’s data in a collaborative environment designed for teams!

![](https://img.shields.io/badge/Coda-EE5A29?style=flat&logo=coda&logoColor=white)  ![](https://img.shields.io/badge/Unity-100000?style=flat&logo=unity&logoColor=white)  ![GitHub](https://img.shields.io/github/license/pamcha/codasync?label=Licence)  ![](https://img.shields.io/badge/Unity%202021.x-supported-blue.svg)  ![](https://img.shields.io/badge/Unity%202022.x-supported-blue.svg)  ![](https://img.shields.io/badge/Unity%206-supported-blue.svg)

<<[Website](https://coda.io/@pamcha/coda-sync "Coda Sync Website") | [Documentation](https://coda.io/@pamcha/coda-sync/documentation-1 "Coda Sync Documentation") | [Tips and tricks](https://coda.io/@pamcha/coda-sync/tips-tricks-9 "Coda Sync Tips and tricks")>>

![Coda Sync Cover](https://i.imgur.com/rDYUp8K.png)

Coda Sync for Unity allows you to synchronize the tables you have created in a coda doc (on coda.io) in your Unity Project.
Your data are imported as Scriptable Objects by automatically generating a class and its instances, and ready to be used anywhere in your code or in the inspector.

You can sync again any time, whenever you have made a change to your data on your doc(s).

![1. Create and design your data](https://i.imgur.com/WMECWzs.png)
![2. Make the perfect database for your game](https://i.imgur.com/Njc1oZc.png)
![3. Setup your Unity project](https://i.imgur.com/3EY5bju.png)
![4. Sync your asset references in your tables](https://i.imgur.com/PPE9n4X.png)
![5. Generate and update your data anytime](https://i.imgur.com/gdIBu1n.png)

⚠️ Please note that you need to create a free account on [coda.io](https://coda.io) to create your docs. Coda.io offers a generous free tier that should cover most of your needs.

🙋Our studio is not related to Coda. We use Coda as makers for several years and have built this asset using the Coda API. Coda provides us a great support for this package.

## Installation
### Requirement
![](https://img.shields.io/badge/Coda-EE5A29?style=flat&logo=coda&logoColor=white)  ![](https://img.shields.io/badge/Unity%202021.x-supported-blue.svg) ![](https://img.shields.io/badge/Unity%202022.x-supported-blue.svg)  ![](https://img.shields.io/badge/Unity%206-supported-blue.svg)

This project has a dependency to `com.unity.editorcoroutines` package that will be automatically installed if not already installed in your project.

### Using Package Manager
In the package Manager Window (`Window/Package Manager`) click on the little plus sign (+) on the top left corner. 
Select "Import package from Git URL" and type `https://github.com/Pamcha/CodaSync.git`.

### Using Git and Manifest.json

Find the manifest.json file in the Packages folder of your project and add a line to `dependencies` field.

* `"com.pamcha.codasync": "https://github.com/Pamcha/CodaSync.git"`

## Technical details
* Converts Coda tables into Unity Scriptable Object classes
* Converts table entries into instances of these classes
* Stores all theses instances in a database class that is easily accessible for your code
* And since these data are Scriptable Objects, they can easily be serialized and use anywhere in your game project

Works on all platform since what you get are Scriptable Objects.

Please note that these Scriptable Objects are generated/updated on the editor, not on runtime. Therefore Coda Sync for Unity is *not* a live Ops tool, but instead a great way to manage all your game data in a collaborative and easy to use environment.